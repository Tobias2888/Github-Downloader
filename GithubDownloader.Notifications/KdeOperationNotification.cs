using System.Globalization;
using System.Threading.Channels;
using GithubDownloader.Core;
using KdeNotificationDBusLib;
using LoggerLib;

namespace GithubDownloader.Notifications;

/// <summary>Best-effort desktop reporting; a slow or unavailable bus never blocks application work.</summary>
public sealed class KdeOperationNotification
{
    private readonly Channel<Snapshot> _updates = Channel.CreateBounded<Snapshot>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true
    });
    private readonly Task<bool> _worker;
    private volatile bool _nativeJobCreated;
    private volatile bool _completionReported;
    private bool _success;
    private bool _cancelled;
    private string _failure = "The operation could not complete. Check GitHub Downloader for details.";

    private KdeOperationNotification(Func<Task<INotificationJob>> connect)
    {
        _worker = Task.Run(async () =>
        {
            try
            {
                await using INotificationJob job = await connect();
                _nativeJobCreated = true;
                await foreach (Snapshot update in _updates.Reader.ReadAllAsync())
                {
                    await job.ReportAsync(update);
                    // Coalesce bursts of extraction and installer output without delaying callers.
                    await Task.Delay(250);
                }
                await job.FinishAsync(_success, _cancelled, _failure);
                _completionReported = true;
                return true;
            }
            catch (Exception exception)
            {
                _nativeJobCreated = false;
                _ = Logger.LogD($"KDE progress unavailable ({exception.GetType().Name})");
                return _completionReported;
            }
        });
    }

    public static KdeOperationNotification? Start(string title, Action? cancel = null, bool enabled = true)
    {
        if (!enabled || !OperatingSystem.IsLinux()
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))) return null;
        return new KdeOperationNotification(() => NativeJob.ConnectAsync(title, cancel));
    }

    internal static KdeOperationNotification StartForTest(Func<Task<INotificationJob>> connect) =>
        new KdeOperationNotification(connect);

    public void ReportTransfer(DownloadProgress progress) => Report(new Snapshot(progress.DisplayText,
        (ulong)Math.Max(0, progress.DownloadedBytes),
        progress.TotalBytes is > 0 ? (ulong)progress.TotalBytes.Value : null,
        double.IsFinite(progress.BytesPerSecond) && progress.BytesPerSecond > 0
            ? (ulong)Math.Min(progress.BytesPerSecond, long.MaxValue) : 0));

    public void ReportStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        // Extraction and legacy download callbacks provide percentages as text.
        int percentIndex = message.IndexOf('%');
        string? prefix = message.StartsWith("Extracting: ", StringComparison.Ordinal) ? "Extracting: "
            : message.StartsWith("Downloaded: ", StringComparison.Ordinal) ? "Downloaded: " : null;
        if (prefix is not null && percentIndex > prefix.Length)
        {
            string number = message[prefix.Length..percentIndex];
            if ((double.TryParse(number, NumberStyles.Float, CultureInfo.CurrentCulture, out double percent)
                || double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
                && double.IsFinite(percent))
            {
                Report(new Snapshot(message, (ulong)(Math.Clamp(percent, 0, 100) * 100), 10_000, 0));
                return;
            }
        }
        Report(new Snapshot(message, 0, null, 0));
    }

    private void Report(Snapshot snapshot) => _updates.Writer.TryWrite(snapshot);

    /// <returns>Whether a native job handles the completion notification.</returns>
    public async Task<bool> FinishAsync(bool success, bool cancelled = false, string? failure = null)
    {
        _success = success;
        _cancelled = cancelled;
        if (!string.IsNullOrWhiteSpace(failure)) _failure = failure;
        _updates.Writer.TryComplete();
        // A desktop service that stops responding must not hold up installation or shutdown.
        try { return await _worker.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException)
        {
            _ = Logger.LogD("KDE progress cleanup continues in the background");
            return _nativeJobCreated || _completionReported;
        }
    }

    public static async Task<bool> RunAsync(string title,
        Func<Action<string>, Action<DownloadProgress>, CancellationToken, Task<bool>> operation,
        Action<string> output, bool cancellable = true)
    {
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        KdeOperationNotification? notification = Start(title, cancellable ? cancellation.Cancel : null);
        bool success = false;
        bool cancelled = false;
        string? failure = null;
        void Status(string message) { output(message); notification?.ReportStatus(message); }
        void Transfer(DownloadProgress progress) { output(progress.DisplayText); notification?.ReportTransfer(progress); }
        try
        {
            success = await operation(Status, Transfer, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            return success;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            cancelled = true;
            output("Downloads cancelled.");
            return false;
        }
        catch (Exception exception) { failure = exception.Message; throw; }
        finally
        {
            if (notification is not null) await notification.FinishAsync(success, cancelled, failure);
        }
    }

    internal sealed record Snapshot(string Description, ulong Processed, ulong? Total, ulong Speed);

    internal interface INotificationJob : IAsyncDisposable
    {
        Task ReportAsync(Snapshot snapshot);
        Task FinishAsync(bool success, bool cancelled, string failure);
    }

    private sealed class NativeJob(KdeJobClient client, KdeJob job, Action? cancel) : INotificationJob
    {
        public static async Task<INotificationJob> ConnectAsync(string title, Action? cancel)
        {
            KdeJobClient client = await KdeJobClient.ConnectAsync();
            try
            {
                KdeJob job = await client.StartAsync(new KdeJobOptions("github-downloader", title)
                {
                    ApplicationName = "GitHub Downloader", IconName = "software-update-available",
                    CanCancel = cancel is not null
                });
                NativeJob native = new NativeJob(client, job, cancel);
                job.CancelRequested += native.OnCancel;
                return native;
            }
            catch { client.Dispose(); throw; }
        }

        private void OnCancel(object? sender, EventArgs args)
        {
            try { cancel?.Invoke(); }
            catch (ObjectDisposedException) { /* The operation finished while the signal was in flight. */ }
        }

        public async Task ReportAsync(Snapshot snapshot)
        {
            await job.SetDescriptionAsync(snapshot.Description);
            await job.ReportAsync(snapshot.Processed, snapshot.Total, snapshot.Speed);
        }

        public Task FinishAsync(bool success, bool cancelled, string failure) =>
            cancelled ? job.CancelAsync() : success ? job.CompleteAsync() : job.FailAsync(failure);

        public async ValueTask DisposeAsync()
        {
            job.CancelRequested -= OnCancel;
            try { await job.DisposeAsync(); }
            finally { client.Dispose(); }
        }
    }
}
