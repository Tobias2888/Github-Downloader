using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    private readonly DownloadStatusViewModel _downloadStatusViewModel;
    
    public HomeView()
    {
        InitializeComponent();
        _mainViewModel = ((App)Application.Current!).MainViewModel;
        _homeViewModel = ((App)Application.Current!).HomeViewModel;
        _downloadStatusViewModel = ((App)Application.Current!).DownloadStatusViewModel;
        DataContext = _homeViewModel;
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        PgbDownloading.DataContext = _downloadStatusViewModel;
        PgbDownloading.Bind(IsVisibleProperty, new Binding(nameof(_downloadStatusViewModel.IsUpdating)));
        
        CheckForGlobalUpdates();
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
            ToastText.Text = $"Failed to fetch repo";
            ToastPopup.IsOpen = true;
            await Task.Delay(2500);
            ToastPopup.IsOpen = false;
            return;
        }
        
        _downloadStatusViewModel.IsUpdating = true;
        await UpdateManager.SearchForUpdates(repo, statusText =>
        {
            _downloadStatusViewModel.StatusText = statusText;
        });
        _downloadStatusViewModel.IsUpdating = false;
        
        UpdateManager.Repos.Add(repo);
        FileManager.SaveRepos();

        TbxUrl.Text = "";
        TbxOwner.Text = "";
        TbxRepo.Text = "";
    }

    private async void BtnSearchForUpdates_OnClick(object? sender, RoutedEventArgs e)
    {
        _downloadStatusViewModel.IsUpdating = true;
        await UpdateManager.SearchForUpdates(UpdateManager.Repos, statusText =>
        {
            _downloadStatusViewModel.StatusText = statusText;
        });
        _downloadStatusViewModel.IsUpdating = false;

        CheckForGlobalUpdates();
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
        
        CheckForGlobalUpdates();
        FileManager.SaveRepos();
    }

    private void CheckForGlobalUpdates()
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
        _mainViewModel.HasUpdates = hasUpdates;
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
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _mainViewModel.SwitchPage(ViewNames.Settings);
    }
}
