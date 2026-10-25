using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Github_Downloader_lib.Models;

namespace Github_Downloader.Views;

public partial class LocalInstallerVersionDialog : Window
{
    public LocalInstallerVersionDialog() => InitializeComponent();

    public LocalInstallerVersionDialog(LocalApp app) : this()
    {
        AppName.Text = app.Name;
        VersionsList.ItemsSource = app.AvailableInstallerVersions;
    }

    private void Versions_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ConfirmInstall is not null)
            ConfirmInstall.IsEnabled = VersionsList.SelectedItem is LocalInstallerVersion version && File.Exists(version.Path);
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Install_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ConfirmInstall.IsEnabled && VersionsList.SelectedItem is LocalInstallerVersion version) Close(version);
    }
}
