using GithubDownloader.Core.Installation;
using GithubDownloader.Core.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FileLib;
using GithubDownloader.Core;
using GithubDownloader.Core.Models;
using GithubDownloader.Desktop.Enums;
using GithubDownloader.Desktop.Models;
using GithubDownloader.Desktop.Services;
using GithubDownloader.Desktop.ViewModels;
using LoggerLib;
using SecretsLib;

namespace GithubDownloader.Desktop;

public partial class App : Application
{
    public MainViewModel MainViewModel { get; } = new();
    public DownloadStatusViewModel DownloadStatusViewModel { get; } = new();
    public HomeViewModel HomeViewModel { get; } = new();
    public RepoDetailsViewModel RepoDetailsViewModel { get; } = new();
    
    public MainWindow? MainWindow;
    private TrayIcon _trayIcon = new();
    private readonly DispatcherTimer _notificationTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    public NotificationService Notifications { get; }

    public App()
    {
        Notifications = new(() => MainViewModel.AppSettings.Notifications,
            message => DesktopNotificationSender.SendAsync(message, ShowMainWindow));
    }

    internal void ShutdownForSelfUpdate()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            throw new InvalidOperationException("Self-updates require the desktop application.");
        MainViewModel.AutoCheckForUpdatesTimer.Stop();
        _notificationTimer.Stop();
        _trayIcon.IsVisible = false;
        desktop.Shutdown();
    }

    public void ShowMainWindow()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ShowMainWindow);
            return;
        }

        if (MainWindow is null)
        {
            MainWindow = new();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = MainWindow;
        }
        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;
        _ = Logger.LogI("Showing main window");
        MainWindow.Show();
        MainWindow.Activate();
    }

    public async Task<bool> RunDownloadsAsync(Func<CancellationToken, Task<bool>> operation, IEnumerable<Repo> repositories, string title = "Downloading updates")
    {
        Repo[] downloads = repositories.ToArray();
        bool succeeded = await DownloadStatusViewModel.RunAsync(operation, title);
        if (succeeded && !DownloadStatusViewModel.CompletionNotificationHandled)
        {
            await Notifications.NotifyDownloadsCompletedAsync(downloads.Length,
                downloads.Length == 1 ? downloads[0].Name : null);
        }
        return succeeded;
    }

    public async Task<bool> UpdateRepoAsync(Repo repo, bool reinstall = false, bool checkForUpdates = false)
    {
        _ = Logger.LogI(reinstall ? "Repository reinstall requested" : "Repository update requested");
        bool performedUpdate = false;
        bool succeeded = await DownloadStatusViewModel.RunAsync(async cancellationToken =>
        {
            if (checkForUpdates && !await UpdateManager.SearchForUpdates(repo, DownloadStatusViewModel.LogStatus))
                return false;

            performedUpdate = reinstall || !repo.IsUpToDate;
            return await UpdateManager.UpdateRepo(repo, DownloadStatusViewModel.LogStatus,
                DownloadStatusViewModel.LogProgress, reinstall, null, cancellationToken,
                selectArchiveExecutable: SelectRepositoryArchiveExecutableAsync, operationProgress: DownloadStatusViewModel.ReportProgress);
        }, $"{(reinstall ? "Reinstalling" : "Updating")} {repo.Name}");
        if (succeeded && performedUpdate && !DownloadStatusViewModel.CompletionNotificationHandled)
            await Notifications.NotifyDownloadsCompletedAsync(1, repo.Name);
        return succeeded;
    }

    public async Task ReinstallAllReposAsync()
    {
        if (DownloadStatusViewModel.IsUpdating) return;
        Repo[] repositories = UpdateManager.Repos.ToArray();
        if (repositories.Length == 0) return;

        _ = Logger.LogI("Reinstall all repositories confirmed");
        try
        {
            await RunDownloadsAsync(async cancellationToken =>
            {
                if (!await UpdateManager.SearchForUpdates(repositories, DownloadStatusViewModel.LogStatus))
                    return false;

                return await UpdateManager.UpdateReposAsync(repositories,
                    DownloadStatusViewModel.LogStatus, DownloadStatusViewModel.LogProgress,
                    downloadAnyways: true, respectExclusions: false,
                    operationProgress: DownloadStatusViewModel.ReportProgress, cancellationToken: cancellationToken,
                    selectArchiveExecutable: SelectRepositoryArchiveExecutableAsync);
            }, repositories, "Reinstalling repositories");
        }
        finally
        {
            FileManager.SaveRepos();
            UpdateIcon();
        }
    }

    internal Task<string?> SelectRepositoryArchiveExecutableAsync(string directory, IReadOnlyList<string> candidates)
    {
        ShowMainWindow();
        return MainWindow is null ? Task.FromResult<string?>(null)
            : ArchiveExecutablePicker.SelectAsync(MainWindow, directory, candidates);
    }

    public async Task UninstallRepoAsync(Repo repo)
    {
        _ = Logger.LogI("Repository uninstall requested");
        if (DownloadStatusViewModel.IsUpdating || !repo.CanUninstall || MainWindow is null)
        {
            _ = Logger.LogW("Repository uninstall unavailable");
            return;
        }
        Views.UninstallRepoDialog dialog = new(repo);
        string? package = await dialog.ShowDialog<string?>(MainWindow);
        if (package is null)
        {
            _ = Logger.LogI("Repository uninstall cancelled");
            return;
        }
        _ = Logger.LogI("Repository uninstall confirmed");

        InstallationManager installations = new();
        await DownloadStatusViewModel.RunAsync(() => installations.UninstallAsync(repo,
            DownloadStatusViewModel.LogStatus, DownloadStatusViewModel.LogProgress, package), "Uninstall Status");
        FileManager.SaveRepos();
        UpdateIcon();
    }
    
    private const string ResPath = "avares://Github-Downloader/resources/";
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            Dispatcher.UIThread.UnhandledException += OnUIThreadUnhandledException;

            InitializeTrayIcon();
            StartApplication();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void OnUIThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _ = Logger.LogE($"Unhandled UI exception: {e.Exception.GetType().Name}");
        if (Application.Current is App app)
        {
            app.MainViewModel.SwitchPage(ViewNames.Home);
        }
        e.Handled = true;
    }

    // Framework initialization cannot await startup; observe failures at this lifecycle boundary.
    private async void StartApplication()
    {
        try { await Start(); }
        catch (Exception exception)
        {
            if (Logger.HasFileBeenCreated)
                await Logger.LogE($"Application startup failed ({exception.GetType().Name})");
        }
    }

    private async Task Start()
    {
        if (!SecretsManager.Initialized)
        {
            SecretsManager.Initialize("hofinga.gh-downloader.secret");
        }
        
        string appdataPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader");
        Logger.LogDir = Path.Join(appdataPath, "logs");
        Logger.LogToTerminal = true;
        Logger.LogFirstChance = false;
        Logger.LogDebug = false;
        Logger.LogLevel = LogLevel.Info;
        // Retention must be configured before LoggerLib creates (and prunes) the session log.
        try
        {
            MainViewModel.AppSettings = AppSettings.Load(writeLogs: false);
        }
        catch (Exception exception)
        {
            await Logger.CreateFileAsync();
            await Logger.LogE($"Application settings load failed ({exception.GetType().Name})");
            throw;
        }
        Logger.KeepLogs = MainViewModel.AppSettings.LogsToKeep;
        await Logger.CreateFileAsync();
        _ = Logger.LogI("Mode: avalonia-desktop-app");
        _ = Logger.LogI("Application settings loaded");
        await InitializeRepositoriesAsync(Program.DesktopActivation,
            async () =>
            {
                await FileManager.LoadRepos(refreshDetails: false);
                WindowsSelfUpdate.Initialize(UpdateManager.Repos);
            },
            async () =>
            {
                await UpdateManager.UpdateRepoDetails(UpdateManager.Repos);
                FileManager.SaveRepos();
            });

        MainViewModel.AutoCheckForUpdatesTimer.Interval = TimeSpan.FromMinutes(MainViewModel.AppSettings.CheckForUpdatesInterval);
        MainViewModel.AutoCheckForUpdatesTimer.Tick += async (_, _) =>
        {
            if (MainWindow?.IsVisible == true)
            {
                return;
            }

            _ = Logger.LogI("AutoCheckForUpdates");
            await DownloadStatusViewModel.RunAsync(() =>
                UpdateManager.SearchForUpdates(UpdateManager.Repos, DownloadStatusViewModel.LogStatus), showNotification: false);
            
            UpdateIcon();
            FileManager.SaveRepos();
            await Notifications.NotifyUpdatesAvailableAsync(UpdateManager.Repos);
        };
        if (MainViewModel.AppSettings.AutoCheckForUpdates)
            MainViewModel.AutoCheckForUpdatesTimer.Start();

        _notificationTimer.Tick += async (_, _) =>
            await Notifications.NotifyUpdatesAvailableAsync(UpdateManager.Repos);
        _notificationTimer.Start();
    }

    internal async Task InitializeRepositoriesAsync(DesktopActivationService? activation,
        Func<Task> loadRepositories, Func<Task> refreshRepositories)
    {
        await loadRepositories();
        UpdateIcon();

        // The cached repository list is ready for binding; network requests must not delay the window.
        activation?.SetShowWindowHandler(ShowMainWindow);

        try
        {
            await refreshRepositories();
            UpdateIcon();
        }
        catch (Exception exception)
        {
            _ = Logger.LogE($"Startup repository refresh failed ({exception.GetType().Name})");
        }
    }

    private void UpdateIcon()
    {
        bool hasUpdates = false;
        foreach (Repo repo in UpdateManager.Repos)
        {
            if (repo.HasRelease && !repo.IsUpToDate)
            {
                hasUpdates = true;
                break;
            }
        }
        MainViewModel.HasUpdates = hasUpdates;
    }
    
    internal void InitializeTrayIcon()
    {
        _trayIcon = new()
        {
            IsVisible = true,
            ToolTipText = "Github Downloader",
            Icon = new(new Bitmap(AssetLoader.Open(new(Path.Join(ResPath + "icon.png")))))
        };

        MainViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MainViewModel.HasUpdates)) return;
            
            _trayIcon.Icon = !MainViewModel.HasUpdates ? 
                new(new Bitmap(AssetLoader.Open(new(Path.Join(ResPath, "icon.png"))))) 
                : new(new Bitmap(AssetLoader.Open(new(Path.Join(ResPath, "icon_update.png")))));
        };

        _trayIcon.Clicked += (_, _) =>
        {
            switch (MainWindow?.IsVisible)
            {
                case true: 
                    _ = Logger.LogI("Hiding main window");
                    MainWindow.Hide(); 
                    MainViewModel.SwitchPage(ViewNames.Home);
                    break;
                default: ShowMainWindow(); break;
            }
        };

        _trayIcon.Menu = [];

        NativeMenuItem updateAllItem = new("Update All");
        updateAllItem.Click += async (_, _) =>
        {
            await RunDownloadsAsync(cancellationToken => UpdateManager.UpdateReposAsync(UpdateManager.Repos,
                DownloadStatusViewModel.LogStatus, DownloadStatusViewModel.LogProgress,
                operationProgress: DownloadStatusViewModel.ReportProgress, cancellationToken: cancellationToken),
                UpdateManager.Repos.Where(repo => !repo.ExcludedFromDownloadAll && !repo.IsUpToDate));
            FileManager.SaveRepos();
        };

        NativeMenuItem separatorItem = new NativeMenuItemSeparator();
        
        NativeMenuItem quitItem = new ("Quit");
        quitItem.Click += async (_, _) =>
        {
            await Logger.LogI("Application quit requested");
            Environment.Exit(0);
        };
        
        _trayIcon.Menu.Add(updateAllItem);
        _trayIcon.Menu.Add(separatorItem);
        _trayIcon.Menu.Add(quitItem);

        // Avalonia creates the native tray icon only when it is attached to the application.
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }
}
