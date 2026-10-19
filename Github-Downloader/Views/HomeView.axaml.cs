using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Github_Downloader.Enums;
using Github_Downloader.ViewModels;
using SecretsLib;
using LoggerLib;

namespace Github_Downloader.Views;

public partial class HomeView : UserControl
{
    private async void InstallLocal_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_downloadStatusViewModel.IsUpdating)
        {
            _downloadStatusViewModel.Show();
            return;
        }
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;
        try
        {
            IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Install local file",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Installers and archives")
                {
                    Patterns = LocalFileInstaller.SupportedExtensions
                        .SelectMany(extension => new[] { "*" + extension, "*" + extension.ToUpperInvariant() })
                        .Append("*.AppImage").Distinct().ToArray()
                }, FilePickerFileTypes.All]
            });
            if (files.Count == 0) return;
            string? path = files[0].TryGetLocalPath();
            if (path is null) throw new IOException("Choose a file stored on this computer.");
            string? destination = null;
            if (ArchiveExtractor.IsSupportedArchive(path))
            {
                IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Extract archive to",
                    AllowMultiple = false
                });
                if (folders.Count == 0) return;
                destination = folders[0].TryGetLocalPath();
                if (destination is null) throw new IOException("Choose a folder on this computer.");
            }
            bool installed = await _downloadStatusViewModel.RunAsync(() => LocalFileInstaller.InstallAsync(path,
                _downloadStatusViewModel.LogStatus, _downloadStatusViewModel.LogProgress, destination), "Install local file");
            LocalAppsPanel.Refresh();
            if (installed) AppTabs.SelectedIndex = 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ToastText.Text = "Could not open the local file. Choose a supported installer or archive.";
            ToastPopup.IsOpen = true;
            await Task.Delay(2500);
            ToastPopup.IsOpen = false;
        }
    }

    private void AppTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source != AppTabs || RepoScroll is null || LocalAppsPanel is null || RepositoryActions is null) return;
        bool localApps = AppTabs.SelectedIndex == 1;
        RepoScroll.IsVisible = !localApps;
        RepositoryActions.IsVisible = !localApps;
        LocalAppsPanel.IsVisible = localApps;
        if (localApps) LocalAppsPanel.Refresh();
    }

    private void History_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner) new HistoryWindow().Show(owner);
    }

    private readonly MainViewModel _mainViewModel;
    private readonly HomeViewModel _homeViewModel;
    private readonly DownloadStatusViewModel _downloadStatusViewModel;
    private readonly RepositoryReorderController _repoReordering;
    
    public HomeView()
    {
        InitializeComponent();
        _mainViewModel = ((App)Application.Current!).MainViewModel;
        _homeViewModel = ((App)Application.Current!).HomeViewModel;
        _downloadStatusViewModel = ((App)Application.Current!).DownloadStatusViewModel;
        DataContext = _homeViewModel;
        _repoReordering = new(this, RepoList, RepoScroll, RepoDragOverlay);
    }

    internal void BeginRepoDrag(RepoEntryControl row, PointerPressedEventArgs e) => _repoReordering.Begin(row, e);

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
        
        await _downloadStatusViewModel.RunAsync(() =>
            UpdateManager.SearchForUpdates(repo, _downloadStatusViewModel.LogStatus));
        
        UpdateManager.Repos.Add(repo);
        FileManager.SaveRepos();
        await ((App)Application.Current!).Notifications.NotifyUpdatesAvailableAsync(UpdateManager.Repos);

        TbxUrl.Text = "";
        TbxOwner.Text = "";
        TbxRepo.Text = "";
    }

    private async void BtnSearchForUpdates_OnClick(object? sender, RoutedEventArgs e)
    {
        await _downloadStatusViewModel.RunAsync(() =>
            UpdateManager.SearchForUpdates(UpdateManager.Repos, _downloadStatusViewModel.LogStatus));

        CheckForGlobalUpdates();
        FileManager.SaveRepos();
        await ((App)Application.Current!).Notifications.NotifyUpdatesAvailableAsync(UpdateManager.Repos);
    }
    
    public async void BtnUpdateAll_OnClick(object? sender, RoutedEventArgs e)
    {
        await ((App)Application.Current!).RunDownloadsAsync(() => UpdateManager.UpdateReposAsync(UpdateManager.Repos,
            _downloadStatusViewModel.LogStatus, _downloadStatusViewModel.LogProgress),
            UpdateManager.Repos.Where(repo => !repo.ExcludedFromDownloadAll && !repo.IsUpToDate));
        
        CheckForGlobalUpdates();
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

    private async void BtnSetPat_OnClick(object? sender, RoutedEventArgs e)
    {
        Logger.LogI("Saving personal access token");
        SecretsManager.StoreSecret("pat", TbxPat.Text);
        Logger.LogI("Personal access token saved");
        TbxPat.Text = "";
        ToastText.Text = "Personal access token saved successfully!";
        ToastPopup.IsOpen = true;
        await Task.Delay(2500);
        ToastPopup.IsOpen = false;
    }

    private void PgbDownloading_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Logger.LogD("Opening operation status");
        _downloadStatusViewModel.Show();
    }

    private void BtnRemovePat_OnClick(object? sender, RoutedEventArgs e)
    {
        Logger.LogI("Removing personal access token");
        SecretsManager.ClearSecret("pat");
        Logger.LogI("Personal access token removed");
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _mainViewModel.SwitchPage(ViewNames.Settings);
    }
}
