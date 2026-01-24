using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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
        
        if (File.Exists(_reposConfigFilePath))
        {
            string jsonString = File.ReadAllText(_reposConfigFilePath);
            _repos = JsonSerializer.Deserialize<List<Repo>>(jsonString);
        }
        else
        {
            _repos = new List<Repo>();
        }
        
        foreach (var repo in _repos)
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
        
        HttpResponseMessage httpResponse = await Api.GetRequest(url, GetPat());
        
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
            DownloadUrl = response.assets[0].url,
            Name = response.name,
            AssetNames = response.assets.ToList().Select(asset  => asset.name).ToList()
        };
        
        _repos.Add(repo);

        SaveRepos();
        CreateTrackedRepoEntry(repo);

        TbxUrl.Text = "";
        TbxOwner.Text = "";
        TbxRepo.Text = "";
    }
    
    private void SaveRepos()
    {
        if (!File.Exists(_reposConfigFilePath))
        {
            FileHelper.Create(_reposConfigFilePath);
        }

        string jsonString = JsonSerializer.Serialize(_repos, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        
        File.WriteAllText(_reposConfigFilePath, jsonString);
    }

    private void CreateTrackedRepoEntry(Repo repo)
    {
        StackPanel stackPanel = new();
        stackPanel.Orientation = Orientation.Horizontal;

        /*
        Button btnUninstall = new();
        btnUninstall.Content = "Uninstall";
        btnUninstall.Background = Brushes.Red;
        */

        Button btnRemove = new();
        btnRemove.Content = "Remove";
        btnRemove.Background = Brushes.Orange;
        btnRemove.Click += (sender, args) =>
        {
            _repos.Remove(repo);
            TrackedRepos.Children.Remove(stackPanel);
            SaveRepos();
        };

        Button btnUpdate = new();
        btnUpdate.Content = "Update";
        btnUpdate.Click += async (sender, args) =>
        {
            InstallDeb(await UpdateRepo(repo));
        };
        
        TextBlock tbxName = new();
        tbxName.Text = repo.Name;

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
                SaveRepos();
            }

        };

        ComboBox cobAssets = new();
        foreach (string assetName in repo.AssetNames)
        {
            ComboBoxItem cobItem = new();
            cobItem.Content = assetName;
            cobAssets.Items.Add(cobItem);
        }
        cobAssets.SelectedIndex = repo.DownloadAssetIndex;

        cobAssets.SelectionChanged += (sender, args) =>
        {
            repo.DownloadAssetIndex = cobAssets.SelectedIndex;
            if (repo.AssetNames[repo.DownloadAssetIndex].Contains(".deb"))
            {
                stackPanel.Children.Remove(btnFilePicker);
            }
            else
            {
                if (btnFilePicker.Parent == null)
                {
                    stackPanel.Children.Add(btnFilePicker);
                }
            }
            SaveRepos();
        };
        
        stackPanel.Children.Add(tbxName);
        stackPanel.Children.Add(cobAssets);
        stackPanel.Children.Add(btnRemove);
        //stackPanel.Children.Add(btnUninstall);
        stackPanel.Children.Add(btnUpdate);

        if (!repo.AssetNames[repo.DownloadAssetIndex].Contains(".deb"))
        {
            stackPanel.Children.Add(btnFilePicker);
        }
        
        TrackedRepos.Children.Add(stackPanel);
    }

    private async void BtnUpdateAll_OnClick(object? sender, RoutedEventArgs e)
    {
        List<string> debPaths = new();
        foreach (Repo repo in _repos)
        {
            debPaths.Add(await UpdateRepo(repo));
        }
        InstallDebs(debPaths);
    }

    private async Task<string> UpdateRepo(Repo repo)
    {
        HttpResponseMessage httpResponse = await Api.GetRequest(repo.Url, GetPat());
        if (!httpResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to fetch release of: {repo.Url}");
            ToastText.Text = $"Failed to fetch release of: {repo.Url}";
            ToastPopup.IsOpen = true;
            await Task.Delay(3000);
            ToastPopup.IsOpen = false;
            return "";
        }
            
        Response response = JsonSerializer.Deserialize<Response>(await httpResponse.Content.ReadAsStringAsync());

        await Api.DownloadFileAsync(repo.DownloadUrl, Path.Join(_cachePath, response.assets[repo.DownloadAssetIndex].name), GetPat());

        if (repo.AssetNames[repo.DownloadAssetIndex].Contains(".deb"))
        {
            return Path.Join(_cachePath, response.assets[repo.DownloadAssetIndex].name);    
        }

        string destPath = Path.Join(repo.DownloadPath, repo.AssetNames[repo.DownloadAssetIndex]);
        if (File.Exists(destPath))
        {
            File.Delete(destPath);
        }
        File.Move(Path.Join(_cachePath, response.assets[repo.DownloadAssetIndex].name), destPath);

        return "";
    }

    private static void InstallDeb(string debPath)
    {
        InstallDebs(new List<string>{debPath});
    }

    private static void InstallDebs(List<string> debPaths)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }
        
        string installCommand = "pkexec apt-get install -y ";
        foreach (string debPath in debPaths)
        {
            if (!debPath.Contains(".deb"))
            {
                continue;
            }
            installCommand += $"\"{debPath}\" ";
        }

        if (installCommand == "pkexec apt-get install -y ")
        {
            return;
        }

        Console.WriteLine(installCommand);
        
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pkexec",
                Arguments = installCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        Console.WriteLine(output);
        Console.WriteLine(error);
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

    private string GetPat()
    {
        return File.ReadAllText(_patFilePath);
    }
}