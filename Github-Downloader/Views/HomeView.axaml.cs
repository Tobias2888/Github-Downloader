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
using Github_Downloader.Services;
using SecretsLib;
using LoggerLib;

namespace Github_Downloader.Views;

public partial class HomeView : UserControl
{
    private bool _installingLocalFiles;

    private async void InstallLocal_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_downloadStatusViewModel.IsUpdating) { _downloadStatusViewModel.Show(); return; }
        if (_installingLocalFiles || TopLevel.GetTopLevel(this) is not TopLevel topLevel) return;
        string[] paths;
        _installingLocalFiles = true;
        try
        {
            IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Install local files",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Installers and archives")
                {
                    Patterns = LocalFileInstaller.SupportedExtensions
                        .SelectMany(extension => new[] { "*" + extension, "*" + extension.ToUpperInvariant() })
                        .Append("*.AppImage").Distinct().ToArray()
                }]
            });
            if (files.Count == 0) return;
            paths = files.Select(file => file.TryGetLocalPath()
                ?? throw new IOException("Choose files stored on this computer."))
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
            if (paths.Any(path => !LocalFileInstaller.SupportedExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException("Choose supported installers or archives.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await ShowLocalFileErrorAsync();
            return;
        }
        finally
        {
            _installingLocalFiles = false;
        }
        await InstallLocalFilesAsync(paths);
    }

    private async Task ShowLocalFileErrorAsync()
    {
        ToastText.Text = "Could not open the local files. Choose supported installers or archives stored on this computer.";
        ToastPopup.IsOpen = true;
        await Task.Delay(2500);
        ToastPopup.IsOpen = false;
    }

    private void LocalFiles_OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        bool accepted = !_installingLocalFiles && !_downloadStatusViewModel.IsUpdating
            && (e.DragEffects & DragDropEffects.Copy) != 0
            && TryGetLocalInstallPaths(e.DataTransfer, out _);
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        LocalFileDropOverlay.IsVisible = accepted;
        e.Handled = true;
    }

    private void LocalFiles_OnDragLeave(object? sender, DragEventArgs e)
    {
        LocalFileDropOverlay.IsVisible = false;
    }

    private async void LocalFiles_OnDrop(object? sender, DragEventArgs e)
    {
        LocalFileDropOverlay.IsVisible = false;
        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        string[] paths = [];
        bool accepted = !_installingLocalFiles && !_downloadStatusViewModel.IsUpdating
            && (e.DragEffects & DragDropEffects.Copy) != 0
            && TryGetLocalInstallPaths(e.DataTransfer, out paths);
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (!accepted) return;
        await InstallLocalFilesAsync(paths);
    }

    internal static bool TryGetLocalInstallPaths(IDataTransfer data, out string[] paths)
    {
        paths = [];
        IStorageItem[] items = data.TryGetFiles()?.ToArray() ?? [];
        if (items.Length == 0) return false;
        List<string> localPaths = [];
        foreach (IStorageItem item in items)
        {
            string? path = item.TryGetLocalPath();
            if (item is not IStorageFile || path is null
                || !LocalFileInstaller.SupportedExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                return false;
            localPaths.Add(path);
        }
        paths = localPaths.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        return true;
    }

    internal async Task InstallLocalFilesAsync(IReadOnlyList<string> paths)
    {
        if (_installingLocalFiles || _downloadStatusViewModel.IsUpdating || paths.Count == 0) return;
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;
        _installingLocalFiles = true;
        try
        {
            List<(string Path, string? Destination)> installations = [];
            foreach (string path in paths)
            {
                if (!File.Exists(path)) throw new FileNotFoundException("The local file no longer exists.");
                string? destination = null;
                if (ArchiveExtractor.IsSupportedArchive(path) && !OperatingSystem.IsLinux())
                {
                    IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = $"Extract {Path.GetFileName(path)} to",
                        AllowMultiple = false
                    });
                    if (folders.Count == 0) return;
                    destination = folders[0].TryGetLocalPath();
                    if (destination is null) throw new IOException("Choose a folder on this computer.");
                }
                installations.Add((path, destination));
            }

            // Recheck after any folder selection, since another operation may have started while the dialog was open.
            if (_downloadStatusViewModel.IsUpdating) return;
            await _downloadStatusViewModel.RunAsync(async () =>
            {
                bool allInstalled = true;
                foreach ((string path, string? destination) in installations)
                {
                    bool succeeded = await LocalFileInstaller.InstallAsync(path,
                        _downloadStatusViewModel.LogStatus, _downloadStatusViewModel.LogProgress, destination,
                        (directory, candidates) => ArchiveExecutablePicker.SelectAsync(this, directory, candidates));
                    allInstalled &= succeeded;
                }
                return allInstalled;
            }, paths.Count == 1 ? "Install local file" : "Install local files");
            LocalAppsPanel.Refresh();
            AppTabs.SelectedIndex = 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await ShowLocalFileErrorAsync();
        }
        finally
        {
            _installingLocalFiles = false;
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
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, LocalFiles_OnDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, LocalFiles_OnDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, LocalFiles_OnDragLeave, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, LocalFiles_OnDrop, RoutingStrategies.Bubble, handledEventsToo: true);
        DetachedFromVisualTree += (_, _) => LocalFileDropOverlay.IsVisible = false;
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
        
        repo.AssetRule = _mainViewModel.AppSettings.DefaultAssetRule;
        repo.DownloadPath = _mainViewModel.AppSettings.GlobalDownloadPath;

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
        await ((App)Application.Current!).RunDownloadsAsync(cancellationToken => UpdateManager.UpdateReposAsync(UpdateManager.Repos,
            _downloadStatusViewModel.LogStatus, _downloadStatusViewModel.LogProgress,
            transferProgress: _downloadStatusViewModel.ReportTransfer, cancellationToken: cancellationToken),
            UpdateManager.Repos.Where(repo => !repo.ExcludedFromDownloadAll && !repo.IsUpToDate));
        
        CheckForGlobalUpdates();
        FileManager.SaveRepos();
    }

    private void CheckForGlobalUpdates()
    {
        bool hasUpdates = false;
        foreach (Repo repo in UpdateManager.Repos)
        {
            if (repo.HasRelease && !repo.IsUpToDate)
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
