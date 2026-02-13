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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Github_Downloader.Enums;
using Github_Downloader.ViewModels;

namespace Github_Downloader.Views;

public partial class HomeView : UserControl
{
    private List<Repo> _repos = ((App)Application.Current!).Repos;
    private readonly MainViewModel _mainViewModel;
    private readonly HomeViewModel _homeViewModel;
    private readonly RepoDetailsViewModel _repoDetailsViewModel;
    private readonly DownloadStatusViewModel _downloadStatusViewModel;
    
    private const string ResPath = "avares://Github-Downloader/resources/";
    
    public HomeView()
    {
        InitializeComponent();
        _mainViewModel = ((App)Application.Current!).MainViewModel;
        _homeViewModel = ((App)Application.Current!).HomeViewModel;
        _repoDetailsViewModel = ((App)Application.Current!).RepoDetailsViewModel;
        _downloadStatusViewModel = App.DownloadStatusViewModel;
        DataContext = _homeViewModel;
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        UpdateManager.Owner = ((App)Application.Current!).MainWindow;

        PgbDownloading.DataContext = _downloadStatusViewModel;
        PgbDownloading.Bind(IsVisibleProperty, new Binding(nameof(_downloadStatusViewModel.IsUpdating)));
        
        LoadGrdTrackedRepos();
    }

    private void SendNotification()
    {
        
        Process.Start(new ProcessStartInfo
        {
            FileName = "notify-send",
            Arguments = $"\"Github-Downloader\" \"Notification\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private void LoadGrdTrackedRepos()
    {
        if (Design.IsDesignMode) return;
        
        foreach (Repo repo in _repos)
        {
            CreateTrackedRepoEntry(repo);
        }
    }

    private async void BtnAddRepo_OnClick(object? sender, RoutedEventArgs e)
    {
        string publisherName = "";
        string repoName = "";
        if (!string.IsNullOrEmpty(TbxUrl.Text))
        {
            try
            {
                string[] values = TbxUrl.Text.Split("github.com/");
                string[] values2 = values[1].TrimEnd('/').Split("/");
                publisherName = values2[0];
                repoName = values2[1];
            }
            catch (Exception) {
                Console.WriteLine($"Failed to parse url: {TbxUrl.Text}");
            }
        }
        else
        {
            publisherName = TbxOwner.Text;
            repoName = TbxRepo.Text;
        }
        
        string url = $"https://api.github.com/repos/{publisherName}/{repoName}/releases/latest";
        string repoUrl = $"https://api.github.com/repos/{publisherName}/{repoName}";
        
        HttpResponseMessage httpRepoResponse = await Api.GetRequest(repoUrl, FileManager.GetPat());
        if (httpRepoResponse == null || !httpRepoResponse.IsSuccessStatusCode)
        {
            Console.WriteLine("Failed to fetch repo");
            ToastText.Text = $"Failed to fetch repo: {repoUrl}";
            ToastPopup.IsOpen = true;
            await Task.Delay(2500);
            ToastPopup.IsOpen = false;
            return;
        }
        
        HttpResponseMessage httpResponse = await Api.GetRequest(url, FileManager.GetPat());
        if (httpResponse == null || !httpResponse.IsSuccessStatusCode)
        {
            Console.WriteLine("Failed to fetch releases");
            ToastText.Text = $"Failed to fetch release of: {url}";
            ToastPopup.IsOpen = true;
            await Task.Delay(2500);
            ToastPopup.IsOpen = false;
            return;
        }
        
        RepoResponse repoResponse = JsonSerializer.Deserialize<RepoResponse>(await httpRepoResponse.Content.ReadAsStringAsync());
        Response response = JsonSerializer.Deserialize<Response>(await httpResponse.Content.ReadAsStringAsync());

        Repo repo = new()
        {
            Url = url,
            Name = repoResponse.full_name,
            Description = repoResponse.description
        };

        await UpdateManager.SearchForUpdates(repo);
        
        _repos.Add(repo);

        FileManager.SaveRepos(_repos);
        CreateTrackedRepoEntry(repo);

        TbxUrl.Text = "";
        TbxOwner.Text = "";
        TbxRepo.Text = "";
    }

    private void CreateTrackedRepoEntry(Repo repo)
    {
        GrdTrackedRepos.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        /*
        Button btnUninstall = new();
        btnUninstall.Content = "Uninstall";
        btnUninstall.Background = Brushes.Red;
        */

        Image imgRemove = new()
        {
            Source = new Bitmap(AssetLoader.Open(new Uri(ResPath + "trash.png"))),
            Width = 25,
            Height = 25,
            Margin = new(10, 0, 0, 0)
        };
        imgRemove.PointerPressed += (_, _) =>
        {
            _repos.Remove(repo);
            GrdTrackedRepos.Children.Clear();
            LoadGrdTrackedRepos();
            FileManager.SaveRepos(_repos);
        };

        Button btnUpdate = new()
        {
            Content = "Update"
        };
        btnUpdate.Click += async (_, _) =>
        {
            await UpdateManager.UpdateRepo(repo);
            FileManager.SaveRepos(_repos);
        };
        
        TextBlock tbxName = new()
        {
            DataContext = repo
        };
        tbxName.Bind(TextBlock.TextProperty, new Binding(nameof(Repo.Name)));

        TextBlock tbxUpdateVersion = new()
        {
            Text = "Version",
            Foreground = Brushes.Orange,
            DataContext = repo
        };
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

        TextBlock tbxVersion = new()
        {
            Text = "Version",
            DataContext = repo
        };
        tbxVersion.Bind(TextBlock.TextProperty, new Binding(nameof(Repo.CurrentInstallTag)));
        tbxVersion.Bind(IsVisibleProperty, new Binding(nameof(Repo.IsUpToDate)));

        StackPanel stpRepoLabel = new()
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                tbxName,
                tbxVersion,
                tbxUpdateVersion,
            }
        };

        Button btnMore = new()
        {
            Content = "More"
        };
        btnMore.Click += (_, _) =>
        {
            _repoDetailsViewModel.Repo = repo;
            _mainViewModel.SwitchPage(ViewNames.RepoDetails);
        };

        ComboBox cobAssets = new()
        {
            Width = 200,
            ItemsSource =  repo.AssetNames,
            SelectedIndex = repo.DownloadAssetIndex,
            Margin = new(10, 0)
        };

        cobAssets.SelectionChanged += (_, _) =>
        {
            repo.DownloadAssetIndex = cobAssets.SelectedIndex;
            FileManager.SaveRepos(_repos);
        };
        
        cobAssets.PropertyChanged += (sender, args) =>
        {
            if (args.Property == ComboBox.ItemCountProperty)
            {
                cobAssets.SelectedIndex = repo.DownloadAssetIndex;
            }
        };

        ToggleSwitch tglExcludeFromDownloadAll = new ToggleSwitch
        {
            OnContent = null,
            OffContent = null,
            IsChecked = repo.ExcludedFromDownloadAll
        };
        tglExcludeFromDownloadAll.Click += (_, _) =>
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
        Grid.SetColumn(btnMore, 1);
        Grid.SetRow(btnMore, GrdTrackedRepos.RowDefinitions.Count -1);
        GrdTrackedRepos.Children.Add(btnMore);
        
        //grid.Children.Add(btnUninstall);
/*
        if (!repo.AssetNames[repo.DownloadAssetIndex].Contains(".deb"))
        {
            Grid.SetColumn(btnFilePicker, 1);
            Grid.SetRow(btnFilePicker, GrdTrackedRepos.RowDefinitions.Count -1);
            GrdTrackedRepos.Children.Add(btnFilePicker);
        }*/
    }

    private async void BtnSearchForUpdates_OnClick(object? sender, RoutedEventArgs e)
    {
        await UpdateManager.SearchForUpdates(_repos);
        FileManager.SaveRepos(_repos);
    }
    
    public async void BtnUpdateAll_OnClick(object? sender, RoutedEventArgs e)
    {
        await UpdateManager.UpdateRepos(_repos);
        FileManager.SaveRepos(_repos);
    }

    private async void BtnSetPat_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TbxPat.Text))
        {
            return;
        }
        File.WriteAllText(_mainViewModel.PatFilePath, TbxPat.Text);
        TbxPat.Text = "";
        ToastText.Text = "Personal access token saved successfully!";
        ToastPopup.IsOpen = true;
        await Task.Delay(2500);
        ToastPopup.IsOpen = false;
    }

    private void PgbDownloading_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        UpdateManager.ShowDialog();
    }

    private void BtnRemovePat_OnClick(object? sender, RoutedEventArgs e)
    {
        File.WriteAllText(_mainViewModel.PatFilePath, "");
    }
}