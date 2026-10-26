using Avalonia.Controls;
using Avalonia.Interactivity;
using Github_Downloader_lib;

namespace Github_Downloader.Views;

public partial class ExportConfigurationDialog : Window
{
    public ExportConfigurationDialog() => InitializeComponent();

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Continue_OnClick(object? sender, RoutedEventArgs e) =>
        Close((InstallerExportMode)InstallerMode.SelectedIndex);
}
