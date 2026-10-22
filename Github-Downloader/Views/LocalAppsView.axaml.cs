using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;

namespace Github_Downloader.Views;

public partial class LocalAppsView : UserControl
{
    public event EventHandler<RoutedEventArgs>? InstallRequested;

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
    private void Refresh_OnClick(object? sender, RoutedEventArgs e) => Refresh();
    private void Install_OnClick(object? sender, RoutedEventArgs e) => InstallRequested?.Invoke(this, e);

    private async void Reinstall_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LocalApp app }) return;
        App application = (App)Application.Current!;
        if (application.DownloadStatusViewModel.IsUpdating) { application.DownloadStatusViewModel.Show(); return; }
        await application.DownloadStatusViewModel.RunAsync(() => new LocalAppManager().ReinstallAsync(app,
            application.DownloadStatusViewModel.LogStatus, application.DownloadStatusViewModel.LogProgress), "Reinstall local app");
        Refresh();
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

    private void SystemUninstaller_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
            ListStatus.Text = "Uninstall the app in system settings, then remove it from this list.";
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ListStatus.Text = "Open your system's installed-apps settings to uninstall this app.";
        }
    }

    private async void Forget_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LocalApp app } || TopLevel.GetTopLevel(this) is not Window owner) return;
        UninstallRepoDialog dialog = new(app, forgetOnly: true);
        if (await dialog.ShowDialog<string?>(owner) is null) return;
        try { LocalApps.Store.Remove(app.Id); Refresh(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ListStatus.Text = "The local app list could not be saved. Check file access.";
        }
    }
}
