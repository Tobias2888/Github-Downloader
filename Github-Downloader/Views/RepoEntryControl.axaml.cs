using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Github_Downloader.Enums;
using Github_Downloader.ViewModels;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using LoggerLib;

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

    private void DragHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            this.FindAncestorOfType<HomeView>()?.BeginRepoDrag(this, e);
    }

    private async void BtnUpdate_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Repo repo) return;

        await ((App)Application.Current!).UpdateRepoAsync(repo);

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
        
        Logger.LogI("Removing repository from tracking");
        UpdateManager.Repos.Remove(repo);
        Logger.LogI("Repository removed from tracking");
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
