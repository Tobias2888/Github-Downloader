using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Github_Downloader.Enums;
using Github_Downloader.ViewModels;
using SecretsLib;

namespace Github_Downloader.Views;

public partial class HomeView : UserControl
{
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
        _downloadStatusViewModel = ((App)Application.Current!).DownloadStatusViewModel;
        DataContext = _homeViewModel;
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
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
        
        foreach (Repo repo in UpdateManager.Repos)
        {
            CreateTrackedRepoEntry(repo);
        }
    }

    private async void BtnAddRepo_OnClick(object? sender, RoutedEventArgs e)
    {
        Repo repo;
        if (!string.IsNullOrEmpty(TbxUrl.Text))
        {
            repo = await UpdateManager.AddRepo(TbxUrl.Text);
        }
        else
        {
            repo = await UpdateManager.AddRepo(TbxOwner.Text, TbxRepo.Text);
        }

        if (repo == null)
        {
            Console.WriteLine("Failed to fetch repo");
            ToastText.Text = $"Failed to fetch repo: {TbxOwner.Text} {TbxRepo.Text}";
            ToastPopup.IsOpen = true;
            await Task.Delay(2500);
            ToastPopup.IsOpen = false;
            
            Console.WriteLine("Failed to fetch releases");
            ToastText.Text = $"Failed to fetch release of: {TbxOwner.Text} {TbxRepo.Text}";
            ToastPopup.IsOpen = true;
            await Task.Delay(2500);
            ToastPopup.IsOpen = false;
        }
        
        _downloadStatusViewModel.IsUpdating = true;
        await UpdateManager.SearchForUpdates(repo, statusText =>
        {
            _downloadStatusViewModel.StatusText = statusText;
        });
        _downloadStatusViewModel.IsUpdating = false;
        
        UpdateManager.Repos.Add(repo);

        FileManager.SaveRepos();
        
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
            UpdateManager.Repos.Remove(repo);
            GrdTrackedRepos.Children.Clear();
            LoadGrdTrackedRepos();
            FileManager.SaveRepos();
        };

        Button btnUpdate = new()
        {
            Content = "Update"
        };
        btnUpdate.Click += async (_, _) =>
        {
            _downloadStatusViewModel.IsUpdating = true;
            await UpdateManager.UpdateRepo(repo, statusText =>
            {
                _downloadStatusViewModel.StatusText = statusText;
            }, progressText =>
            {
                _downloadStatusViewModel.ProgressText = progressText;
            });
            _downloadStatusViewModel.IsUpdating = false;
            FileManager.SaveRepos();
        };

        TextBlock tbxName = new()
        {
            DataContext = repo,
            VerticalAlignment = VerticalAlignment.Center
        };
        tbxName.Bind(TextBlock.TextProperty, new Binding(nameof(Repo.Name)));

        TextBlock tbxUpdateVersion = new()
        {
            Text = "Version",
            Foreground = Brushes.Orange,
            DataContext = repo,
            VerticalAlignment = VerticalAlignment.Center
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
            DataContext = repo,
            VerticalAlignment = VerticalAlignment.Center
        };
        tbxVersion.Bind(TextBlock.TextProperty, new Binding(nameof(Repo.CurrentInstallTag)));
        tbxVersion.Bind(IsVisibleProperty, new Binding(nameof(Repo.IsUpToDate)));

        Grid grdRepoLabel = new();
        
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
        
        grdRepoLabel.Children.Add(stpRepoLabel);

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
            FileManager.SaveRepos();
        };
        
        cobAssets.PropertyChanged += (sender, args) =>
        {
            if (args.Property == ComboBox.ItemCountProperty)
            {
                cobAssets.SelectedIndex = repo.DownloadAssetIndex;
            }
        };

        ToggleSwitch tglExcludeFromUpdateAll = new()
        {
            OnContent = null,
            OffContent = null,
            IsChecked = repo.ExcludedFromDownloadAll
        };
        tglExcludeFromUpdateAll.Click += (_, _) =>
        {
            repo.ExcludedFromDownloadAll = tglExcludeFromUpdateAll.IsChecked == true;
            FileManager.SaveRepos();
        };

        CheckBox ckbUpdate = new()
        {
            IsChecked = !repo.ExcludedFromDownloadAll
        };
        ckbUpdate.Click += (_, _) =>
        {
            repo.ExcludedFromDownloadAll = ckbUpdate.IsChecked == false;
            FileManager.SaveRepos();
        };

        int column = 0;
        Grid.SetColumn(ckbUpdate, column++);
        Grid.SetRow(ckbUpdate, GrdTrackedRepos.RowDefinitions.Count - 1);
        GrdTrackedRepos.Children.Add(ckbUpdate);
        Grid.SetColumn(grdRepoLabel, column++);
        Grid.SetRow(grdRepoLabel, GrdTrackedRepos.RowDefinitions.Count -1);
        GrdTrackedRepos.Children.Add(grdRepoLabel);
        Grid.SetColumn(btnMore, column++);
        Grid.SetRow(btnMore, GrdTrackedRepos.RowDefinitions.Count -1);
        GrdTrackedRepos.Children.Add(btnMore);
        Grid.SetColumn(cobAssets, column++);
        Grid.SetRow(cobAssets, GrdTrackedRepos.RowDefinitions.Count -1);
        GrdTrackedRepos.Children.Add(cobAssets);
        /*Grid.SetColumn(tglExcludeFromUpdateAll, column++);
        Grid.SetRow(tglExcludeFromUpdateAll, GrdTrackedRepos.RowDefinitions.Count -1);
        GrdTrackedRepos.Children.Add(tglExcludeFromUpdateAll);*/
        Grid.SetColumn(btnUpdate, column++);
        Grid.SetRow(btnUpdate, GrdTrackedRepos.RowDefinitions.Count -1);
        GrdTrackedRepos.Children.Add(btnUpdate);
        Grid.SetColumn(imgRemove, column++);
        Grid.SetRow(imgRemove, GrdTrackedRepos.RowDefinitions.Count -1);
        GrdTrackedRepos.Children.Add(imgRemove);
        
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
        _downloadStatusViewModel.IsUpdating = true;
        await UpdateManager.SearchForUpdates(UpdateManager.Repos, statusText =>
        {
            _downloadStatusViewModel.StatusText = statusText;
        });
        _downloadStatusViewModel.IsUpdating = false;
        FileManager.SaveRepos();
    }
    
    public async void BtnUpdateAll_OnClick(object? sender, RoutedEventArgs e)
    {
        _downloadStatusViewModel.IsUpdating = true;
        await UpdateManager.UpdateRepos(UpdateManager.Repos, statusText =>
        {
            _downloadStatusViewModel.StatusText = statusText;
        }, progressText =>
        {
            _downloadStatusViewModel.ProgressText = progressText;
        });
        _downloadStatusViewModel.IsUpdating = false;
        FileManager.SaveRepos();
    }

    private async void BtnSetPat_OnClick(object? sender, RoutedEventArgs e)
    {
        SecretsManager.StoreSecret("pat", TbxPat.Text);
        TbxPat.Text = "";
        ToastText.Text = "Personal access token saved successfully!";
        ToastPopup.IsOpen = true;
        await Task.Delay(2500);
        ToastPopup.IsOpen = false;
    }

    private void PgbDownloading_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _downloadStatusViewModel.ShowDialog();
    }

    private void BtnRemovePat_OnClick(object? sender, RoutedEventArgs e)
    {
        SecretsManager.ClearSecret("pat");
        //File.WriteAllText(_mainViewModel.PatFilePath, "");
    }
}