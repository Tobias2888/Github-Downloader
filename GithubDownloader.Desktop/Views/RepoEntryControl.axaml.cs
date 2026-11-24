using GithubDownloader.Core.Configuration;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using GithubDownloader.Desktop.Enums;
using GithubDownloader.Desktop.ViewModels;
using GithubDownloader.Core;
using GithubDownloader.Core.Models;
using GithubDownloader.Desktop.Services;
using LoggerLib;

namespace GithubDownloader.Desktop.Views;

public partial class RepoEntryControl : UserControl
{
    private readonly MainViewModel _mainViewModel;
    private readonly RepoDetailsViewModel _repoDetailsViewModel;
    private readonly DownloadStatusViewModel _downloadStatusViewModel;

    public RepoEntryControl()
    {
        InitializeComponent();
        this.FindControl<Border>("DragHandle")!.Cursor = SystemDragCursors.OpenHand;
        _mainViewModel = ((App)Application.Current!).MainViewModel;
        _repoDetailsViewModel = ((App)Application.Current!).RepoDetailsViewModel;
        _downloadStatusViewModel = ((App)Application.Current!).DownloadStatusViewModel;
        DataContextChanged += (_, _) => UpdateLauncherButtons();
        UpdateLauncherButtons();
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

    private void UpdateLauncherButtons()
    {
        bool available = DataContext is Repo repo && RepoAppRuntime.CanOpen(repo);
        this.FindControl<Button>("BtnOpen")!.IsVisible = available;
        this.FindControl<Button>("BtnLogs")!.IsVisible = available;
    }

    private void BtnOpen_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Repo repo) return;
        if (!RepoAppRuntime.TryOpen(repo, out RepoAppLogSession? session, out string error) || session is null)
        {
            _ = Logger.LogW(error);
            this.FindAncestorOfType<HomeView>()?.ShowToast(error);
        }
    }

    private void BtnLogs_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Repo repo) return;
        RepoAppLogSession session = RepoAppRuntime.GetSession(repo) ?? new RepoAppLogSession(repo.Name);
        if (!session.HasOutput) session.Add("ghd", "No output has been captured. Use Open to start this app from GHD.");
        RepoAppLogsWindow window = new(session);
        if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner);
        else window.Show();
    }

    private void BtnRemove_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Repo repo) return;
        
        _ = Logger.LogI("Removing repository from tracking");
        UpdateManager.Repos.Remove(repo);
        _ = Logger.LogI("Repository removed from tracking");
        FileManager.SaveRepos();
    }

    private void CheckForGlobalUpdates()
    {
        bool hasUpdates = false;
        foreach (Repo repo in UpdateManager.Repos)
        {
            if (!repo.IsUpToDate)
            {
                hasUpdates = true;
                break;
            }
        }
        _mainViewModel.HasUpdates = hasUpdates;
    }
}
