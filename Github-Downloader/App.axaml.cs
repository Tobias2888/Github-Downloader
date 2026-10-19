using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FileLib;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Github_Downloader.Enums;
using Github_Downloader.Models;
using Github_Downloader.Services;
using Github_Downloader.ViewModels;
using LoggerLib;
using SecretsLib;

namespace Github_Downloader;

public partial class App : Application
{
    public MainViewModel MainViewModel { get; } = new();
    public DownloadStatusViewModel DownloadStatusViewModel { get; } = new();
    public HomeViewModel HomeViewModel { get; } = new();
    public RepoDetailsViewModel RepoDetailsViewModel { get; } = new();
    
    public MainWindow? MainWindow;
    private TrayIcon _trayIcon;
    private readonly DispatcherTimer _notificationTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    public NotificationService Notifications { get; }

    public App()
    {
        Notifications = new(() => MainViewModel.AppSettings.Notifications,
            message => DesktopNotificationSender.SendAsync(message, ShowMainWindow));
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
        Logger.LogI("Showing main window");
        MainWindow.Show();
        MainWindow.Activate();
    }

    public async Task<bool> RunDownloadsAsync(Func<Task<bool>> operation, IEnumerable<Repo> repositories)
    {
        Repo[] downloads = repositories.ToArray();
        bool succeeded = await DownloadStatusViewModel.RunAsync(operation);
        if (succeeded)
        {
            await Notifications.NotifyDownloadsCompletedAsync(downloads.Length,
                downloads.Length == 1 ? downloads[0].Name : null);
        }
        return succeeded;
    }

    public async Task<bool> UpdateRepoAsync(Repo repo, bool reinstall = false, bool checkForUpdates = false)
    {
        Logger.LogI(reinstall ? "Repository reinstall requested" : "Repository update requested");
        bool performedUpdate = false;
        bool succeeded = await DownloadStatusViewModel.RunAsync(async () =>
        {
            if (checkForUpdates && !await UpdateManager.SearchForUpdates(repo, DownloadStatusViewModel.LogStatus))
                return false;

            performedUpdate = reinstall || !repo.IsUpToDate;
            return await UpdateManager.UpdateRepo(repo, DownloadStatusViewModel.LogStatus,
                DownloadStatusViewModel.LogProgress, reinstall);
        });
        if (succeeded && performedUpdate)
            await Notifications.NotifyDownloadsCompletedAsync(1, repo.Name);
        return succeeded;
    }

    public async Task UninstallRepoAsync(Repo repo)
    {
        Logger.LogI("Repository uninstall requested");
        if (DownloadStatusViewModel.IsUpdating || !repo.CanUninstall || MainWindow is null)
        {
            Logger.LogW("Repository uninstall unavailable");
            return;
        }
        Views.UninstallRepoDialog dialog = new(repo);
        string? package = await dialog.ShowDialog<string?>(MainWindow);
        if (package is null)
        {
            Logger.LogI("Repository uninstall cancelled");
            return;
        }
        Logger.LogI("Repository uninstall confirmed");

        InstallationManager installations = new();
        await DownloadStatusViewModel.RunAsync(() => installations.UninstallAsync(repo,
            DownloadStatusViewModel.LogStatus, DownloadStatusViewModel.LogProgress, package), "Uninstall Status");
        FileManager.SaveRepos();
        UpdateIcon();
    }
    
    private const string ResPath = "avares://Github-Downloader/resources/";
    private string _appdataPath;
    private string _reposConfigFilePath;
    
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
            Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void OnUIThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.LogE($"Unhandled UI exception: {e.Exception.GetType().Name}");
        if (Application.Current is App app)
        {
            app.MainViewModel.SwitchPage(ViewNames.Home);
        }
        e.Handled = true;
    }

    private async Task Start()
    {
        if (!SecretsManager.Initialized)
        {
            SecretsManager.Initialize("hofinga.gh-downloader.secret");
        }
        
        _appdataPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader");
        _reposConfigFilePath = Path.Join(_appdataPath, "repos.json");
        Logger.LogDir = Path.Join(_appdataPath, "logs");
        Logger.LogToTerminal = true;
        Logger.LogFirstChance = false;
        Logger.LogDebug = false;
        Logger.LogLevel = LogLevel.Info;
        await Logger.CreateFile();
        Logger.LogI("Mode: avalonia-desktop-app");
        MainViewModel.AppSettings = AppSettings.Load();
        await InitializeRepositoriesAsync(Program.DesktopActivation,
            () => FileManager.LoadRepos(refreshDetails: false),
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

            Logger.LogI("AutoCheckForUpdates");
            await DownloadStatusViewModel.RunAsync(() =>
                UpdateManager.SearchForUpdates(UpdateManager.Repos, DownloadStatusViewModel.LogStatus));
            
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
            Logger.LogE($"Startup repository refresh failed ({exception.GetType().Name})");
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
    
    private void InitializeTrayIcon()
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
                    Logger.LogI("Hiding main window");
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
            await RunDownloadsAsync(() => UpdateManager.UpdateReposAsync(UpdateManager.Repos,
                DownloadStatusViewModel.LogStatus, DownloadStatusViewModel.LogProgress),
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
    }
}
