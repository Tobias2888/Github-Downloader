using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Github_Downloader.Services;
using Github_Downloader.Views;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

[Collection("Repository dragging")]
public sealed class DesktopStartupTests
{
    private readonly RepositoryDragApplication _application;

    public DesktopStartupTests(RepositoryDragApplication application) => _application = application;

    [Fact]
    public async Task StartupAttachesTheConfiguredTrayIconToTheApplication()
    {
        await _application.Dispatch(() =>
        {
            App app = Assert.IsAssignableFrom<App>(Application.Current);
            TrayIcons? previousIcons = TrayIcon.GetIcons(app);
            try
            {
                app.InitializeTrayIcon();

                TrayIcon icon = Assert.Single(TrayIcon.GetIcons(app)!);
                Assert.True(icon.IsVisible);
                Assert.NotNull(icon.Icon);
                Assert.Equal("Github Downloader", icon.ToolTipText);
                Assert.Collection(icon.Menu!.Items,
                    item => Assert.Equal("Update All", Assert.IsType<NativeMenuItem>(item).Header),
                    item => Assert.IsType<NativeMenuItemSeparator>(item),
                    item => Assert.Equal("Quit", Assert.IsType<NativeMenuItem>(item).Header));
            }
            finally
            {
                TrayIcon.SetIcons(app, previousIcons);
            }

            return Task.CompletedTask;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("--gui")]
    [InlineData("--hidden")]
    public async Task WindowIsReadyWhileStartupGitHubRequestsArePending(string? argument)
    {
        await _application.Dispatch(async () =>
        {
            App app = Assert.IsAssignableFrom<App>(Application.Current);
            ObservableCollection<Repo> originalRepositories = UpdateManager.Repos;
            MainWindow? originalWindow = app.MainWindow;
            app.MainWindow = null;
            ObservableCollection<Repo> cachedRepositories =
            [
                new Repo { Name = "cached/repository", Url = "https://api.github.com/repos/cached/repository" }
            ];
            TaskCompletionSource localDataReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource refreshStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource refreshFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            string[] args = argument is null ? [] : [argument];
            bool showWindow = Program.ShouldShowWindow(args);
            using DesktopActivationService activation = new(NewInstanceName(), showWindow);
            Task startup = app.InitializeRepositoriesAsync(activation,
                async () =>
                {
                    await localDataReady.Task;
                    UpdateManager.Repos = cachedRepositories;
                },
                () =>
                {
                    refreshStarted.SetResult();
                    return refreshFinished.Task;
                });

            try
            {
                Assert.Null(app.MainWindow);
                Assert.False(refreshStarted.Task.IsCompleted);
                localDataReady.SetResult();
                await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                Assert.False(startup.IsCompleted);
                Assert.Equal(showWindow, app.MainWindow?.IsVisible ?? false);

                // A --gui handoff must also open a hidden instance before its network refresh finishes.
                Assert.True(await activation.ActivateAsync(TimeSpan.FromSeconds(5)));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.True(app.MainWindow!.IsVisible);
                app.MainWindow.UpdateLayout();
                HomeView home = Assert.Single(app.MainWindow.GetVisualDescendants().OfType<HomeView>());
                Assert.Same(cachedRepositories, home.FindControl<ItemsControl>("RepoList")!.ItemsSource);
                Assert.False(startup.IsCompleted);
            }
            finally
            {
                localDataReady.TrySetResult();
                refreshFinished.TrySetResult();
                await startup.WaitAsync(TimeSpan.FromSeconds(5));
                app.MainWindow?.Hide();
                app.MainWindow = originalWindow;
                UpdateManager.Repos = originalRepositories;
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FailedStartupRefreshKeepsTheWindowAvailable()
    {
        await _application.Dispatch(async () =>
        {
            App app = Assert.IsAssignableFrom<App>(Application.Current);
            ObservableCollection<Repo> originalRepositories = UpdateManager.Repos;
            MainWindow? originalWindow = app.MainWindow;
            app.MainWindow = null;
            using DesktopActivationService activation = new(NewInstanceName());
            try
            {
                await app.InitializeRepositoriesAsync(activation,
                    () =>
                    {
                        UpdateManager.Repos = [];
                        return Task.CompletedTask;
                    },
                    () => Task.FromException(new HttpRequestException("GitHub unavailable")));

                Assert.True(app.MainWindow!.IsVisible);
                app.MainWindow.Hide();
                Assert.True(await activation.ActivateAsync(TimeSpan.FromSeconds(5)));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.True(app.MainWindow.IsVisible);
            }
            finally
            {
                app.MainWindow?.Hide();
                app.MainWindow = originalWindow;
                UpdateManager.Repos = originalRepositories;
            }
        }, CancellationToken.None);
    }

    private static string NewInstanceName() => "ghd-startup-test-" + Guid.NewGuid().ToString("N");
}
