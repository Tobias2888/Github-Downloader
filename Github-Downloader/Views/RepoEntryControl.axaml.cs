using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Github_Downloader.Enums;
using Github_Downloader.ViewModels;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;

namespace Github_Downloader.Views;

public partial class RepoEntryControl : UserControl
{
    private readonly MainViewModel _mainViewModel;
    private readonly RepoDetailsViewModel _repoDetailsViewModel;
    private readonly DownloadStatusViewModel _downloadStatusViewModel;

    public RepoEntryControl()
    {
        InitializeComponent();
        _mainViewModel = ((App)Application.Current!).MainViewModel;
        _repoDetailsViewModel = ((App)Application.Current!).RepoDetailsViewModel;
        _downloadStatusViewModel = ((App)Application.Current!).DownloadStatusViewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void BtnUpdate_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Repo repo) return;

        _downloadStatusViewModel.IsUpdating = true;
        await UpdateManager.UpdateRepo(repo, statusText =>
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

    private void BtnMore_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Repo repo) return;
        
        _repoDetailsViewModel.Repo = repo;
        _mainViewModel.SwitchPage(ViewNames.RepoDetails);
    }

    private void BtnRemove_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Repo repo) return;
        
        UpdateManager.Repos.Remove(repo);
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
}
