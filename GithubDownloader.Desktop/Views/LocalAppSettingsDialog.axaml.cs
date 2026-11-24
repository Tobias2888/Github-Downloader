using GithubDownloader.Core.LocalApplications;
using GithubDownloader.Core.Models;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace GithubDownloader.Desktop.Views;

public partial class LocalAppSettingsDialog : Window
{
    private readonly LocalApp _app;
    private readonly LocalAppManager _manager = new();

    public LocalAppSettingsDialog() : this(new LocalApp
    {
        Id = "preview", FileName = "app.zip", InstallationName = "local/app", Kind = LocalAppKind.Archive
    }) { }

    public LocalAppSettingsDialog(LocalApp app)
    {
        InitializeComponent();
        _app = app;
        DataContext = app;
        AppTitle.Text = app.Name + " settings";
    }

    private App ApplicationInstance => (App)Avalonia.Application.Current!;

    private async void Arguments_OnClick(object? sender, RoutedEventArgs e)
    {
        DesktopEntryArgumentsDialog dialog = new(_app.DesktopEntryArguments, async arguments =>
        {
            if (ApplicationInstance.DownloadStatusViewModel.IsUpdating) throw new InvalidOperationException("Wait for the current operation to finish.");
            await _manager.SetDesktopEntryArgumentsAsync(_app, arguments, _ => { });
        });
        await dialog.ShowDialog(this);
    }

    private async void DesktopEntryName_OnClick(object? sender, RoutedEventArgs e)
    {
        DesktopEntryNameDialog dialog = new(_app.DesktopEntryName, async name =>
        {
            if (ApplicationInstance.DownloadStatusViewModel.IsUpdating) throw new InvalidOperationException("Wait for the current operation to finish.");
            await _manager.SetDesktopEntryNameAsync(_app, name, _ => { });
        });
        await dialog.ShowDialog(this);
    }

    private async void ChooseIcon_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Choose an icon for {_app.Name}", AllowMultiple = false,
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(_app.ExtractionDirectory),
                FileTypeFilter = [new FilePickerFileType("Application icons")
                {
                    Patterns = ["*.png", "*.svg", "*.xpm", "*.PNG", "*.SVG", "*.XPM"]
                }]
            });
            if (files.Count == 0) return;
            string path = files[0].TryGetLocalPath() ?? throw new IOException("Choose an icon stored on this computer.");
            if (ApplicationInstance.DownloadStatusViewModel.IsUpdating) return;
            await ApplicationInstance.DownloadStatusViewModel.RunAsync(async () =>
            {
                await _manager.SetArchiveIconAsync(_app, path, ApplicationInstance.DownloadStatusViewModel.LogProgress);
                return true;
            }, "Change archive app icon");
            SettingsStatus.Text = "Icon updated.";
            SettingsStatus.Foreground = Avalonia.Media.Brushes.LightGreen;
            SettingsStatus.IsVisible = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SettingsStatus.Text = $"Could not change the archive app icon: {exception.Message}";
            SettingsStatus.IsVisible = true;
        }
    }

    private void Done_OnClick(object? sender, RoutedEventArgs e) => Close();
}
