using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FileLib;
using Github_Downloader.Enums;
using Github_Downloader.ViewModels;

namespace Github_Downloader;

public partial class App : Application
{
    public MainViewModel MainViewModel { get; } = new();
    public DownloadStatusViewModel DownloadStatusViewModel { get; } = new();
    public HomeViewModel HomeViewModel { get; } = new();
    public RepoDetailsViewModel RepoDetailsViewModel { get; } = new();
    
    public List<Repo> Repos;
    
    public MainWindow? MainWindow;
    private TrayIcon _trayIcon;
    
    private const string ResPath = "avares://Github-Downloader/resources/";
    private string _appdataPath;
    private string _reposConfigFilePath;
    
    private readonly int UpdateInterval = 5;
    
    public override void Initialize()
    {
        if (Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Length > 1)
        {
            Console.WriteLine("Service already running");
            //Environment.Exit(0);
        }
        AvaloniaXamlLoader.Load(this);
    }

    public async override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            
            InitializeTrayIcon();
            await Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task Start()
    {
        _appdataPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader");
        _reposConfigFilePath = Path.Join(_appdataPath, "repos.json");
        
        if (File.Exists(_reposConfigFilePath))
        {
            string jsonString = File.ReadAllText(_reposConfigFilePath);
            Repos = JsonSerializer.Deserialize<List<Repo>>(jsonString);
        }

        Repos ??= [];
        
        DispatcherTimer timer = new();
        timer.Tick += async (_, _) =>
        {
            if (MainWindow?.IsVisible == true)
            {
                return;
            }
            await UpdateManager.SearchForUpdates(Repos);
            FileManager.SaveRepos(Repos);
        };
        timer.Interval = TimeSpan.FromMinutes(UpdateInterval);
        timer.Start();
        
        await UpdateManager.UpdateRepoDetails(Repos);
        FileManager.SaveRepos(Repos);

        await UpdateManager.SearchForUpdates(Repos);
        FileManager.SaveRepos(Repos);
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
            await UpdateManager.UpdateRepos(Repos);
            FileManager.SaveRepos(Repos);
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