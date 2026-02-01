using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using FileLib;

namespace Github_Downloader;

public partial class MainWindow : Window
{
    private List<Repo> _repos;
    private string _appdataPath;
    private string _cachePath;
    private string _reposConfigFilePath;
    private string _patFilePath;
    
    private TrayIcon _trayIcon;
    private UpdateManager _updateManager;
    
    private const string ResPath = "avares://Github-Downloader/resources";
    
    public MainWindow() => InitializeComponent();

    private async void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        Hide();
        InitializeTrayIcon();
        
        _appdataPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader");
        _cachePath = Path.Join(DirectoryHelper.GetCacheDirPath(), "github-downloader");
        _reposConfigFilePath = Path.Join(_appdataPath, "repos.json");
        _patFilePath = Path.Join(_appdataPath, "pat");
        DirectoryHelper.CreateDir(_appdataPath);
        DirectoryHelper.CreateDir(_cachePath);

        _updateManager = new UpdateManager
        {
            CachePath = _cachePath,
            Owner = this
        };
        
        if (File.Exists(_reposConfigFilePath))
        {
            string jsonString = File.ReadAllText(_reposConfigFilePath);
            _repos = JsonSerializer.Deserialize<List<Repo>>(jsonString);
        }
        else
        {
            _repos = new List<Repo>();
        }
        
        LoadGrdTrackedRepos();

        /*
        DispatcherTimer timer = new();
        timer.Tick += (_, _) => SendNotification();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Start();
        */
    }

    private void SendNotification()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "notify-send",
            Arguments = $"\"Github-Downloader\" \"Norification\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private void LoadGrdTrackedRepos()
    {
        foreach (Repo repo in _repos)
        {
            CreateTrackedRepoEntry(repo);
        }
    }

    private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.WindowClosing)
        {
            Hide();
            e.Cancel = true;
        }
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            IsVisible = true,
            ToolTipText = "Github Downloader",
            Icon = new WindowIcon(AppDomain.CurrentDomain.BaseDirectory + "icon.png")
        };

        _trayIcon.Clicked += (sender, args) =>
        {
            switch (IsVisible)
            {
                case true: Hide(); break;
                case false: Show(); break;
            }
        };

        _trayIcon.Menu = new NativeMenu();

        NativeMenuItem updateAllItem = new("Update All");
        updateAllItem.Click += (sender, args) =>
        {
            BtnUpdateAll_OnClick(sender, null);
        };

        NativeMenuItem seperatorItem = new NativeMenuItemSeparator();
        
        NativeMenuItem quitItem = new ("Quit");
        quitItem.Click += (sender, args) =>
        {
            Environment.Exit(0);
        };
        
        _trayIcon.Menu.Add(updateAllItem);
        _trayIcon.Menu.Add(seperatorItem);
        _trayIcon.Menu.Add(quitItem);
    }

    private async void BtnAddRepo_OnClick(object? sender, RoutedEventArgs e)
    {
        string url;
        if (!string.IsNullOrEmpty(TbxUrl.Text))
        {
            try
            {
                string[] values = TbxUrl.Text.Split("github.com/");
                string[] values2 = values[1].TrimEnd('/').Split("/");
                url = $"https://api.github.com/repos/{values2[0]}/{values2[1]}/releases/latest";
            }
            catch (Exception ignored) {
                Console.WriteLine($"Failed to parse url: {TbxUrl.Text}");
                url = "";
            }
        }
        else
        {
            url = $"https://api.github.com/repos/{TbxOwner.Text}/{TbxRepo.Text}/releases/latest";
        }
        
        HttpResponseMessage httpResponse = await Api.GetRequest(url, FileManager.GetPat());
        
        if (httpResponse == null || !httpResponse.IsSuccessStatusCode)
        {
            Console.WriteLine("Failed to fetch releases");
            ToastText.Text = $"Failed to fetch release of: {TbxUrl.Text}";
            ToastPopup.IsOpen = true;
            await Task.Delay(2500);
            ToastPopup.IsOpen = false;
            return;
        }
        
        Response response = JsonSerializer.Deserialize<Response>(await httpResponse.Content.ReadAsStringAsync());

        Repo repo = new()
        {
            Url = url,
            Name = response.name,
            AssetNames = response.assets.ToList().Select(asset  => asset.name).ToList(),
            DownloadUrls = response.assets.ToList().Select(asset => asset.url).ToList(),
            Tag = response.tag_name
        };
        
        _repos.Add(repo);

        FileManager.SaveRepos(_repos);
        CreateTrackedRepoEntry(repo);

        TbxUrl.Text = "";
        TbxOwner.Text = "";
        TbxRepo.Text = "";
    }

    private void CreateTrackedRepoEntry(Repo repo)
    {
        GrdTrackedRepos.RowDefinitions.Add(new RowDefinition());

        /*
        Button btnUninstall = new();
        btnUninstall.Content = "Uninstall";
        btnUninstall.Background = Brushes.Red;
        */

        Image imgRemove = new()
        {
            Source = new Bitmap(AssetLoader.Open(new Uri(Path.Join(ResPath, "trash.png")))),
            Width = 25,
            Height = 25,
            Margin = new Thickness(10, 0, 0, 0)
        };
        imgRemove.PointerPressed += (sender, args) =>
        {
            _repos.Remove(repo);
            GrdTrackedRepos.Children.Clear();
            LoadGrdTrackedRepos();
            FileManager.SaveRepos(_repos);
        };

        Button btnUpdate = new();
        btnUpdate.Content = "Update";
        btnUpdate.Click += async (sender, args) =>
        {
            await _updateManager.UpdateRepo(repo);
            FileManager.SaveRepos(_repos);
        };
        
        TextBlock tbxName = new();
        tbxName.DataContext = repo;
        tbxName.Bind(TextBlock.TextProperty, new Binding(nameof(Repo.Name)));

        TextBlock tbxUpdateVersion = new();
        tbxUpdateVersion.Text = "Version";
        tbxUpdateVersion.Foreground = Brushes.Orange;
        tbxUpdateVersion.DataContext = repo;
        tbxUpdateVersion.Bind(
            TextBlock.TextProperty, 
            new MultiBinding
            {
                StringFormat = "{0} -> {1}",
                Bindings =
                {
                    new Binding(nameof(Repo.CurrentInstallTag)),
                    new Binding(nameof(Repo.Tag))
                }
            });
        tbxUpdateVersion.Bind(IsVisibleProperty, new Binding("!" + nameof(Repo.IsUpToDate)));

        StackPanel stpRepoLabel = new()
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                tbxName,
                tbxUpdateVersion,
            }
        };

        Button btnFilePicker = new();
        btnFilePicker.Content = "Select download location";
        btnFilePicker.Background = Brushes.CornflowerBlue;
        btnFilePicker.Click += async (sender, args) =>
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
            {
                return;
            }

            IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Select folder",
                    AllowMultiple = false
                });

            if (folders.Count > 0)
            {
                string path = folders[0].Path.LocalPath;
                repo.DownloadPath = path;
                FileManager.SaveRepos(_repos);
            }
        };

        ComboBox cobAssets = new()
        {
            Width = 200,
            ItemsSource =  repo.AssetNames
        };
        cobAssets.SelectedIndex = repo.DownloadAssetIndex;

        cobAssets.SelectionChanged += (sender, args) =>
        {
            repo.DownloadAssetIndex = cobAssets.SelectedIndex;
            if (repo.AssetNames[repo.DownloadAssetIndex].Contains(".deb"))
            {
                GrdTrackedRepos.Children.Remove(btnFilePicker);
            }
            else
            {
                if (btnFilePicker.Parent == null)
                {
                    GrdTrackedRepos.Children.Add(btnFilePicker);
                }
            }
            FileManager.SaveRepos(_repos);
        };

        ToggleSwitch tglExcludeFromDownloadAll = new ToggleSwitch
        {
            OnContent = null,
            OffContent = null
        };
        tglExcludeFromDownloadAll.IsChecked = repo.ExcludedFromDownloadAll;
        tglExcludeFromDownloadAll.Click += (sender, args) =>
        {
            repo.ExcludedFromDownloadAll = tglExcludeFromDownloadAll.IsChecked == true;
            FileManager.SaveRepos(_repos);
        };
        
        Grid.SetColumn(stpRepoLabel, 0);
        Grid.SetRow(stpRepoLabel, GrdTrackedRepos.RowDefinitions.Count -1);
        GrdTrackedRepos.Children.Add(stpRepoLabel);
        Grid.SetColumn(cobAssets, 2);
        Grid.SetRow(cobAssets, GrdTrackedRepos.RowDefinitions.Count -1);
        GrdTrackedRepos.Children.Add(cobAssets);
        Grid.SetColumn(tglExcludeFromDownloadAll, 3);
        Grid.SetRow(tglExcludeFromDownloadAll, GrdTrackedRepos.RowDefinitions.Count -1);
        GrdTrackedRepos.Children.Add(tglExcludeFromDownloadAll);
        Grid.SetColumn(btnUpdate, 4);
        Grid.SetRow(btnUpdate, GrdTrackedRepos.RowDefinitions.Count -1);
        GrdTrackedRepos.Children.Add(btnUpdate);
        Grid.SetColumn(imgRemove, 5);
        Grid.SetRow(imgRemove, GrdTrackedRepos.RowDefinitions.Count -1);
        GrdTrackedRepos.Children.Add(imgRemove);
        //grid.Children.Add(btnUninstall);

        if (!repo.AssetNames[repo.DownloadAssetIndex].Contains(".deb"))
        {
            Grid.SetColumn(btnFilePicker, 1);
            Grid.SetRow(btnFilePicker, GrdTrackedRepos.RowDefinitions.Count -1);
            GrdTrackedRepos.Children.Add(btnFilePicker);
        }
    }

    private async void BtnSearchForUpdates_OnClick(object? sender, RoutedEventArgs e)
    {
        await _updateManager.SearchForUpdates(_repos);
        FileManager.SaveRepos(_repos);
    }
    
    private async void BtnUpdateAll_OnClick(object? sender, RoutedEventArgs e)
    {
        await _updateManager.UpdateRepos(_repos);
        FileManager.SaveRepos(_repos);
    }

    private async void BtnSetPat_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TbxPat.Text))
        {
            return;
        }
        File.WriteAllText(_patFilePath, TbxPat.Text);
        TbxPat.Text = "";
        ToastText.Text = "Personal access token saved successfuly!";
        ToastPopup.IsOpen = true;
        await Task.Delay(2500);
        ToastPopup.IsOpen = false;
    }
}