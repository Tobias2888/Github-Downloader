using Avalonia.Controls;
using Avalonia.Interactivity;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;

namespace Github_Downloader.Views;

public partial class UninstallRepoDialog : Window
{
    private readonly Repo? _repo;
    private readonly LocalApp? _localApp;
    private readonly bool _forgetOnly;
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

    public UninstallRepoDialog(LocalApp app, bool forgetOnly = false) : this()
    {
        _localApp = app;
        _forgetOnly = forgetOnly;
        _repo = app.ToRepo();
        DataContext = _repo;
        AppName.Text = app.Name;
        RepositoryNotice.IsVisible = false;
        if (forgetOnly)
        {
            Title = "Remove local app entry";
            DialogQuestion.Text = "Remove from local apps?";
            ConfirmUninstall.Content = "Remove from list";
            PackageFields.IsVisible = false;
        }
        UpdateConfirmation();
    }

    private void PackageName_OnTextChanged(object? sender, TextChangedEventArgs e) => UpdateConfirmation();

    private void UpdateConfirmation()
    {
        if (ConfirmUninstall is null) return;
        if (_localApp is not null)
            UninstallDetails.Text = _forgetOnly ? "Removes this entry from the list. Installed files are kept."
                : new LocalAppManager().GetUninstallDescription(_localApp);
        else if (_repo is not null)
            UninstallDetails.Text = _installations.GetUninstallDescription(_repo, PackageName.Text);
        ConfirmUninstall.IsEnabled = _forgetOnly || _repo is not null && (_localApp?.CanUninstall ?? _repo.CanUninstall)
            && (!_repo.NeedsDebianPackageName || InstallationManager.IsValidDebianPackageName(PackageName.Text?.Trim()));
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Uninstall_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ConfirmUninstall.IsEnabled) Close(PackageName.Text?.Trim() ?? string.Empty);
    }
}
