using System;
using System.Diagnostics;
using System.IO;
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
    
    private const string ResPath = "avares://Github-Downloader/resources/";
    private string _appdataPath;
    private string _reposConfigFilePath;
    
    public override void Initialize()
    {
        if (Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Length > 1)
        {
            #if !DEBUG
                Console.WriteLine("Service already running");
                Environment.Exit(0);
            #endif
        }
        
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            
            InitializeTrayIcon();
            Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task Start()
    {
        if (!SecretsManager.Initialized)
        {
            SecretsManager.Initialize("hofinga.gh-downloader.secret");
        }
        MainViewModel.AppSettings = AppSettings.Load();
        
        _appdataPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader");
        _reposConfigFilePath = Path.Join(_appdataPath, "repos.json");
        Logger.LogDir = Path.Join(_appdataPath, "logs");
        Logger.LogToTerminal = true;
        //Logger.LogFirstChance = false;
        Logger.CreateFile();
        
        await FileManager.LoadRepos();
        UpdateIcon();

        MainViewModel.AutoCheckForUpdatesTimer.Interval = TimeSpan.FromMinutes(MainViewModel.AppSettings.CheckForUpdatesInterval);
        MainViewModel.AutoCheckForUpdatesTimer.Tick += (_, _) =>
        {
            if (MainWindow?.IsVisible == true)
            {
                return;
            }

            Logger.LogI("AutoCheckForUpdates");
            DownloadStatusViewModel.IsUpdating = true;
            UpdateManager.SearchForUpdates(UpdateManager.Repos, statusText =>
            {
                DownloadStatusViewModel.StatusText = statusText;
            });
            DownloadStatusViewModel.IsUpdating = false;
            
            UpdateIcon();
            FileManager.SaveRepos();
        };
        MainViewModel.AutoCheckForUpdatesTimer.Start();
    }

    private void UpdateIcon()
    {
        bool hasUpdates = false;
        foreach (Repo repo in UpdateManager.Repos)
        {
            if (repo.CurrentInstallTag != repo.Tag)
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
            if (MainWindow is null)
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    MainWindow = new();
                    desktop.MainWindow = MainWindow;
                }
            }
            switch (MainWindow?.IsVisible)
            {
                case true: 
                    MainWindow.Hide(); 
                    MainViewModel.SwitchPage(ViewNames.Home);
                    break;
                case false: MainWindow.Show(); break;
            }
        };

        _trayIcon.Menu = [];

        NativeMenuItem updateAllItem = new("Update All");
        updateAllItem.Click += async (_, _) =>
        {
            DownloadStatusViewModel.IsUpdating = true;
            await UpdateManager.UpdateReposAsync(UpdateManager.Repos, statusText =>
            {
                DownloadStatusViewModel.StatusText = statusText;
            }, progressText =>
            {
                DownloadStatusViewModel.ProgressText = progressText;
            });
            DownloadStatusViewModel.IsUpdating = false;
            FileManager.SaveRepos();
        };

        NativeMenuItem separatorItem = new NativeMenuItemSeparator();
        
        NativeMenuItem quitItem = new ("Quit");
        quitItem.Click += (_, _) =>
        {
            Environment.Exit(0);
        };
        
        _trayIcon.Menu.Add(updateAllItem);
        _trayIcon.Menu.Add(separatorItem);
        _trayIcon.Menu.Add(quitItem);
    }
}