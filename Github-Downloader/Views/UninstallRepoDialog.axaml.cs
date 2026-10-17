using Avalonia.Controls;
using Avalonia.Interactivity;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;

namespace Github_Downloader.Views;

public partial class UninstallRepoDialog : Window
{
    private readonly Repo? _repo;
    private readonly InstallationManager _installations = new();

    public UninstallRepoDialog()
    {
        InitializeComponent();
    }

    public UninstallRepoDialog(Repo repo) : this()
    {
        _repo = repo;
        DataContext = repo;
        UpdateConfirmation();
    }

    private void PackageName_OnTextChanged(object? sender, TextChangedEventArgs e) => UpdateConfirmation();

    private void UpdateConfirmation()
    {
        if (ConfirmUninstall is null) return;
        if (_repo is not null)
            UninstallDetails.Text = _installations.GetUninstallDescription(_repo, PackageName.Text);
        ConfirmUninstall.IsEnabled = _repo is not null && _repo.CanUninstall && (!_repo.NeedsDebianPackageName
            || InstallationManager.IsValidDebianPackageName(PackageName.Text?.Trim()));
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Uninstall_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ConfirmUninstall.IsEnabled) Close(PackageName.Text?.Trim() ?? string.Empty);
    }
}
