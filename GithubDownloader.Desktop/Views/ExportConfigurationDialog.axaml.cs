using GithubDownloader.Core.Configuration;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GithubDownloader.Core;

namespace GithubDownloader.Desktop.Views;

public partial class ExportConfigurationDialog : Window
{
    public ExportConfigurationDialog() => InitializeComponent();

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Continue_OnClick(object? sender, RoutedEventArgs e) =>
        Close((InstallerExportMode)InstallerMode.SelectedIndex);
}
