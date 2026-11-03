using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Github_Downloader.Services;

internal sealed class DesktopActivationService : IDisposable
{
    private readonly Mutex _instanceMutex;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _activationLock = new();
    private readonly Task? _listener;
    private Action? _showWindow;
    private bool _showPending;
    private bool _disposed;

    public string InstanceName { get; }
    public bool IsPrimaryInstance { get; }

    public DesktopActivationService(string instanceName, bool showWindow = true)
    {
        InstanceName = instanceName;
        _instanceMutex = new(false, instanceName + "-instance");
        try
        {
            IsPrimaryInstance = _instanceMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            IsPrimaryInstance = true;
        }

        if (IsPrimaryInstance)
        {
            _showPending = showWindow;
            try
            {
                NamedPipeServerStream server = CreateServer();
                _listener = Task.Run(() => ListenAsync(server));
            }
            catch
            {
                _instanceMutex.ReleaseMutex();
                _instanceMutex.Dispose();
                _shutdown.Dispose();
                throw;
            }
        }
    }

    public static DesktopActivationService ForCurrentUser(bool showWindow)
    {
        string userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        byte[] userHash = SHA256.HashData(Encoding.UTF8.GetBytes(userDirectory));
        string instanceName = "github-downloader-" + Convert.ToHexString(userHash.AsSpan(0, 16));
        return new(instanceName, showWindow);
    }

    public void SetShowWindowHandler(Action showWindow)
    {
        bool showPending;
        lock (_activationLock)
        {
            _showWindow = showWindow;
            showPending = _showPending;
            _showPending = false;
        }

        if (showPending)
            showWindow();
    }

    public async Task<bool> ActivateAsync(TimeSpan timeout)
    {
        using CancellationTokenSource deadline = new(timeout);
        while (!deadline.IsCancellationRequested)
        {
            try
            {
                using NamedPipeClientStream client = new(".", InstanceName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
                using StreamWriter writer = new(client, Encoding.UTF8, leaveOpen: true);
                using StreamReader reader = new(client, Encoding.UTF8, leaveOpen: true);
                await writer.WriteLineAsync("show".AsMemory(), deadline.Token).ConfigureAwait(false);
                await writer.FlushAsync(deadline.Token).ConfigureAwait(false);
                return await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false) == "ok";
            }
            catch (IOException)
            {
                // The primary may be between connections while starting or activating.
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            try
            {
                await Task.Delay(50, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    private NamedPipeServerStream CreateServer() => new(InstanceName, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task ListenAsync(NamedPipeServerStream firstServer)
    {
        NamedPipeServerStream server = firstServer;
        while (true)
        {
            using (server)
            {
                try
                {
                    await server.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                    using CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                    request.CancelAfter(TimeSpan.FromSeconds(5));
                    using StreamReader reader = new(server, Encoding.UTF8, leaveOpen: true);
                    using StreamWriter writer = new(server, Encoding.UTF8, leaveOpen: true);
                    if (await reader.ReadLineAsync(request.Token).ConfigureAwait(false) == "show")
                    {
                        RequestShowWindow();
                        await writer.WriteLineAsync("ok".AsMemory(), request.Token).ConfigureAwait(false);
                        await writer.FlushAsync(request.Token).ConfigureAwait(false);
                    }
                }
                catch (IOException)
                {
                    // A client may exit before reading the acknowledgement.
                }
                catch (OperationCanceledException)
                {
                    // Resume listening after an incomplete request, or stop on shutdown.
                }
            }

            if (_shutdown.IsCancellationRequested)
                break;

            server = CreateServer();
        }
    }

    private void RequestShowWindow()
    {
        Action? showWindow;
        lock (_activationLock)
        {
            showWindow = _showWindow;
            if (showWindow is null)
                _showPending = true;
        }

        showWindow?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _shutdown.Cancel();
        try
        {
            _listener?.GetAwaiter().GetResult();
        }
        finally
        {
            if (IsPrimaryInstance)
                _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
            _shutdown.Dispose();
        }
    }
}
