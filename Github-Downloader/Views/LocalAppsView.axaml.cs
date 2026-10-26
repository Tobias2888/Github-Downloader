using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Github_Downloader.Services;

namespace Github_Downloader.Views;

public partial class LocalAppsView : UserControl
{
    public event EventHandler<RoutedEventArgs>? InstallRequested;
    private bool _addingVersion;

    public LocalAppsView() => InitializeComponent();

    public void Refresh()
    {
        try
        {
            IReadOnlyList<LocalApp> apps = LocalApps.Store.Read();
            AppsList.ItemsSource = apps;
            ListStatus.Text = apps.Count == 0 ? "No local apps installed yet." : "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppsList.ItemsSource = Array.Empty<LocalApp>();
            ListStatus.Text = "The local app list could not be read. Check file access.";
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => Refresh();
    private void Install_OnClick(object? sender, RoutedEventArgs e) => InstallRequested?.Invoke(this, e);

    private async void Reinstall_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LocalApp app }) return;
        App application = (App)Application.Current!;
        if (application.DownloadStatusViewModel.IsUpdating) { application.DownloadStatusViewModel.Show(); return; }
        await application.DownloadStatusViewModel.RunAsync(() => new LocalAppManager().ReinstallAsync(app,
            application.DownloadStatusViewModel.LogStatus, application.DownloadStatusViewModel.LogProgress,
            (directory, candidates) => ArchiveExecutablePicker.SelectAsync(this, directory, candidates)), "Reinstall local app");
        Refresh();
    }

    private async void AddVersion_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_addingVersion || sender is not Button { DataContext: LocalApp app }
            || TopLevel.GetTopLevel(this) is not Window owner) return;
        App application = (App)Application.Current!;
        if (application.DownloadStatusViewModel.IsUpdating) { application.DownloadStatusViewModel.Show(); return; }
        _addingVersion = true;
        try
        {
            IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Add a version of {app.Name}",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Archives")
                {
                    Patterns = ArchiveExtractor.SupportedExtensions
                        .SelectMany(extension => new[] { "*" + extension, "*" + extension.ToUpperInvariant() }).ToArray()
                }]
            });
            if (files.Count == 0) return;
            string path = files[0].TryGetLocalPath() ?? throw new IOException("Choose an archive stored on this computer.");
            string? destination = null;
            if (!OperatingSystem.IsLinux() && string.IsNullOrWhiteSpace(app.ExtractionDirectory))
            {
                IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = $"Extract {Path.GetFileName(path)} to", AllowMultiple = false
                });
                if (folders.Count == 0) return;
                destination = folders[0].TryGetLocalPath() ?? throw new IOException("Choose a folder on this computer.");
            }
            if (application.DownloadStatusViewModel.IsUpdating) return;
            await application.DownloadStatusViewModel.RunAsync(() => new LocalAppManager().InstallNewArchiveVersionAsync(app, path,
                application.DownloadStatusViewModel.LogStatus, application.DownloadStatusViewModel.LogProgress,
                (directory, candidates) => ArchiveExecutablePicker.SelectAsync(this, directory, candidates), destination), "Install new archive version");
            Refresh();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ListStatus.Text = $"Could not add the archive version: {exception.Message}";
        }
        finally { _addingVersion = false; }
    }

    private async void Uninstall_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LocalApp app } || TopLevel.GetTopLevel(this) is not Window owner) return;
        App application = (App)Application.Current!;
        if (application.DownloadStatusViewModel.IsUpdating) { application.DownloadStatusViewModel.Show(); return; }
        UninstallRepoDialog dialog = new(app);
        string? package = await dialog.ShowDialog<string?>(owner);
        if (package is null) return;
        await application.DownloadStatusViewModel.RunAsync(() => new LocalAppManager().UninstallAsync(app,
            application.DownloadStatusViewModel.LogStatus, application.DownloadStatusViewModel.LogProgress, package), "Uninstall local app");
        Refresh();
    }

    private async void InstallVersion_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LocalApp app } || TopLevel.GetTopLevel(this) is not Window owner) return;
        App application = (App)Application.Current!;
        if (application.DownloadStatusViewModel.IsUpdating) { application.DownloadStatusViewModel.Show(); return; }
        LocalInstallerVersionDialog dialog = new(app);
        LocalInstallerVersion? version = await dialog.ShowDialog<LocalInstallerVersion?>(owner);
        if (version is null) return;
        if (application.DownloadStatusViewModel.IsUpdating) { application.DownloadStatusViewModel.Show(); return; }
        await application.DownloadStatusViewModel.RunAsync(() => new LocalAppManager().InstallVersionAsync(app, version,
            application.DownloadStatusViewModel.LogStatus, application.DownloadStatusViewModel.LogProgress,
            (directory, candidates) => ArchiveExecutablePicker.SelectAsync(this, directory, candidates)), "Install saved version");
        Refresh();
    }

    private void SystemUninstaller_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
            ListStatus.Text = "Uninstall the app in system settings. Its GHD entry and saved installer are kept for reinstalling.";
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ListStatus.Text = "Open your system's installed-apps settings to uninstall this app.";
        }
    }

    private async void Remove_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LocalApp app } || TopLevel.GetTopLevel(this) is not Window owner) return;
        App application = (App)Application.Current!;
        if (application.DownloadStatusViewModel.IsUpdating) { application.DownloadStatusViewModel.Show(); return; }
        UninstallRepoDialog dialog = new(app, removeOnly: true);
        if (await dialog.ShowDialog<string?>(owner) is null) return;
        if (application.DownloadStatusViewModel.IsUpdating) { application.DownloadStatusViewModel.Show(); return; }
        try { LocalApps.Store.Remove(app.Id); Refresh(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ListStatus.Text = "The entry or saved installer could not be removed. Check file access.";
        }
    }
}
