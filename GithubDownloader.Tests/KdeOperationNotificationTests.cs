using GithubDownloader.Core;
using GithubDownloader.Notifications;
using Xunit;

namespace GithubDownloader.Tests;

public sealed class KdeOperationNotificationTests
{
    [Fact]
    public async Task UnavailableBusDoesNotFailTheOperation()
    {
        KdeOperationNotification notification = KdeOperationNotification.StartForTest(
            () => Task.FromException<KdeOperationNotification.INotificationJob>(new IOException("Bus unavailable")));
        notification.ReportTransfer(new DownloadProgress(50, 100, 10, null));
        Assert.False(await notification.FinishAsync(true));
    }

    [Fact]
    public async Task LatestTransferAndCompletionAreDelivered()
    {
        TaskCompletionSource<KdeOperationNotification.INotificationJob> connection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeJob job = new();
        KdeOperationNotification notification = KdeOperationNotification.StartForTest(() => connection.Task);
        for (int index = 0; index < 1000; index++)
            notification.ReportTransfer(new DownloadProgress(index, 1000, 123, null));
        connection.SetResult(job);
        Assert.True(await notification.FinishAsync(true));
        KdeOperationNotification.Snapshot snapshot = Assert.Single(job.Updates);
        Assert.Equal(999UL, snapshot.Processed);
        Assert.Equal(1000UL, snapshot.Total);
        Assert.Equal(123UL, snapshot.Speed);
        Assert.True(job.Success);
        Assert.True(job.Disposed);
    }

    [Theory]
    [InlineData("Installing package", 0UL, null)]
    [InlineData("Extracting: 35%", 3500UL, 10000UL)]
    [InlineData("Downloaded: 45.50%", 4550UL, 10000UL)]
    public async Task PhaseChangesClearTransferDataAndExtractionReportsPercent(string message, ulong processed, ulong? total)
    {
        TaskCompletionSource<KdeOperationNotification.INotificationJob> connection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeJob job = new();
        KdeOperationNotification notification = KdeOperationNotification.StartForTest(() => connection.Task);
        notification.ReportTransfer(new DownloadProgress(99, 100, 50, null));
        notification.ReportStatus(message);
        connection.SetResult(job);
        Assert.True(await notification.FinishAsync(true));
        KdeOperationNotification.Snapshot snapshot = Assert.Single(job.Updates);
        Assert.Equal(message, snapshot.Description);
        Assert.Equal(processed, snapshot.Processed);
        Assert.Equal(total, snapshot.Total);
        Assert.Equal(0UL, snapshot.Speed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedAndCancelledOperationsHaveDistinctOutcomes(bool cancelled)
    {
        FakeJob job = new();
        KdeOperationNotification notification = KdeOperationNotification.StartForTest(
            () => Task.FromResult<KdeOperationNotification.INotificationJob>(job));
        await notification.FinishAsync(false, cancelled, "Installation failed");
        Assert.False(job.Success);
        Assert.Equal(cancelled, job.Cancelled);
        Assert.Equal("Installation failed", job.Failure);
    }

    [Fact]
    public async Task ReportingFailureIsIsolatedAndDisposesTheConnection()
    {
        FakeJob job = new() { ThrowOnReport = true };
        KdeOperationNotification notification = KdeOperationNotification.StartForTest(
            () => Task.FromResult<KdeOperationNotification.INotificationJob>(job));
        notification.ReportStatus("Installing");
        Assert.False(await notification.FinishAsync(true));
        Assert.True(job.Disposed);
    }

    [Fact]
    public async Task CompletionFailureKeepsFallbackAvailable()
    {
        FakeJob job = new() { ThrowOnFinish = true };
        KdeOperationNotification notification = KdeOperationNotification.StartForTest(
            () => Task.FromResult<KdeOperationNotification.INotificationJob>(job));
        Assert.False(await notification.FinishAsync(true));
        Assert.True(job.Disposed);
    }

    [Fact]
    public async Task CleanupFailureAfterNativeCompletionDoesNotCauseDuplicateNotification()
    {
        FakeJob job = new() { ThrowOnDispose = true };
        KdeOperationNotification notification = KdeOperationNotification.StartForTest(
            () => Task.FromResult<KdeOperationNotification.INotificationJob>(job));
        Assert.True(await notification.FinishAsync(true));
        Assert.True(job.Success);
    }

    private sealed class FakeJob : KdeOperationNotification.INotificationJob
    {
        public List<KdeOperationNotification.Snapshot> Updates { get; } = [];
        public bool Success { get; private set; }
        public bool Cancelled { get; private set; }
        public string? Failure { get; private set; }
        public bool Disposed { get; private set; }
        public bool ThrowOnReport { get; init; }
        public bool ThrowOnFinish { get; init; }
        public bool ThrowOnDispose { get; init; }
        public Task ReportAsync(KdeOperationNotification.Snapshot snapshot)
        {
            if (ThrowOnReport) throw new IOException("Disconnected");
            Updates.Add(snapshot);
            return Task.CompletedTask;
        }
        public Task FinishAsync(bool success, bool cancelled, string failure)
        {
            if (ThrowOnFinish) throw new IOException("Disconnected before completion");
            Success = success;
            Cancelled = cancelled;
            Failure = failure;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (ThrowOnDispose) throw new IOException("Disconnected during cleanup");
            return ValueTask.CompletedTask;
        }
    }
}
