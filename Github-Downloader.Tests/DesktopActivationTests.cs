using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System.IO.Pipes;
using Github_Downloader.Services;
using Xunit;

namespace Github_Downloader.Tests;

// A named mutex must be acquired and released on the same thread.
#pragma warning disable xUnit1031
public class DesktopActivationTests
{
    [Fact]
    public void HiddenStartupStaysHiddenUntilActivation()
    {
        using DesktopActivationService primary = new(NewInstanceName(), showWindow: false);
        int showCount = 0;
        primary.SetShowWindowHandler(() => Interlocked.Increment(ref showCount));
        Assert.True(primary.IsPrimaryInstance);
        Assert.Equal(0, showCount);

        Assert.True(primary.ActivateAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        Assert.Equal(1, showCount);
        Assert.True(primary.ActivateAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        Assert.Equal(2, showCount);
    }

    [Fact]
    public void DefaultStartupShowsWindowWhenInitializationFinishes()
    {
        using DesktopActivationService primary = new(NewInstanceName());
        int showCount = 0;
        Assert.Equal(0, showCount);
        primary.SetShowWindowHandler(() => showCount++);
        Assert.Equal(1, showCount);
    }

    [Theory]
    [InlineData(new string[] { }, true)]
    [InlineData(new string[] { "--gui" }, true)]
    [InlineData(new string[] { "--hidden" }, false)]
    [InlineData(new string[] { "--gui", "--hidden" }, false)]
    [InlineData(new string[] { "--hidden", "--gui" }, false)]
    public void StartupArgumentsControlInitialWindowVisibility(string[] args, bool expectedVisible)
    {
        bool showWindow = Program.ShouldShowWindow(args);
        using DesktopActivationService primary = new(NewInstanceName(), showWindow);
        int showCount = 0;
        primary.SetShowWindowHandler(() => showCount++);

        Assert.Equal(expectedVisible ? 1 : 0, showCount);
    }

    [Fact]
    public void ActivationDuringStartupIsQueuedUntilWindowIsReady()
    {
        using DesktopActivationService primary = new(NewInstanceName(), showWindow: false);
        Assert.True(primary.ActivateAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        int showCount = 0;
        primary.SetShowWindowHandler(() => showCount++);
        Assert.Equal(1, showCount);
    }

    [Fact]
    public void ConcurrentStartUsesExistingInstanceAndExitReleasesOwnership()
    {
        string instanceName = NewInstanceName();
        using (DesktopActivationService primary = new(instanceName))
        {
            bool secondaryIsPrimary = true;
            Exception? error = null;
            Thread secondaryThread = new(() =>
            {
                try
                {
                    using DesktopActivationService secondary = new(instanceName);
                    secondaryIsPrimary = secondary.IsPrimaryInstance;
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            });
            secondaryThread.Start();
            Assert.True(secondaryThread.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(error);
            Assert.False(secondaryIsPrimary);
        }

        using DesktopActivationService replacement = new(instanceName);
        Assert.True(replacement.IsPrimaryInstance);
    }

    [Fact]
    public void ShutdownCancelsAnIncompleteActivationRequest()
    {
        string instanceName = NewInstanceName();
        using DesktopActivationService primary = new(instanceName);
        using NamedPipeClientStream client = new(".", instanceName, PipeDirection.InOut, PipeOptions.Asynchronous);
        client.ConnectAsync(5000).GetAwaiter().GetResult();
        primary.Dispose();

        using DesktopActivationService replacement = new(instanceName);
        Assert.True(replacement.IsPrimaryInstance);
    }

    private static string NewInstanceName() => "ghd-activation-test-" + Guid.NewGuid().ToString("N");
}
#pragma warning restore xUnit1031

[Collection("Repository dragging")]
public class DesktopActivationWindowTests
{
    private readonly RepositoryDragApplication _application;

    public DesktopActivationWindowTests(RepositoryDragApplication application) => _application = application;

    [Theory]
    [InlineData(WindowState.Minimized, WindowState.Normal)]
    [InlineData(WindowState.Normal, WindowState.Normal)]
    [InlineData(WindowState.Maximized, WindowState.Maximized)]
    public async Task OpeningTheWindowRestoresAVisibleSavedState(WindowState savedState, WindowState expectedState)
    {
        await _application.Dispatch(async () =>
        {
            App app = Assert.IsAssignableFrom<App>(Application.Current);
            WindowState originalState = app.MainViewModel.AppSettings.WindowState;
            MainWindow? originalWindow = app.MainWindow;
            app.MainWindow = null;
            app.MainViewModel.AppSettings.WindowState = savedState;
            try
            {
                app.ShowMainWindow();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.True(app.MainWindow!.IsVisible);
                Assert.Equal(expectedState, app.MainWindow.WindowState);

                app.MainWindow.WindowState = WindowState.Minimized;
                app.ShowMainWindow();
                Assert.Equal(WindowState.Normal, app.MainWindow.WindowState);
            }
            finally
            {
                app.MainWindow?.Hide();
                app.MainWindow = originalWindow;
                app.MainViewModel.AppSettings.WindowState = originalState;
            }
        }, CancellationToken.None);
    }
}
