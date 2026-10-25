using Avalonia.Controls;
using Avalonia.Interactivity;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;

namespace Github_Downloader.Views;

public partial class UninstallRepoDialog : Window
{
    private readonly Repo? _repo;
    private readonly LocalApp? _localApp;
    private readonly bool _removeOnly;
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

    public UninstallRepoDialog(LocalApp app, bool removeOnly = false) : this()
    {
        _localApp = app;
        _removeOnly = removeOnly;
        _repo = app.ToRepo();
        DataContext = _repo;
        AppName.Text = app.Name;
        RepositoryNotice.IsVisible = false;
        if (removeOnly)
        {
            Title = "Remove local app entry";
            DialogQuestion.Text = "Remove entry and saved installers?";
            ConfirmUninstall.Content = "Remove";
            PackageFields.IsVisible = false;
        }
        UpdateConfirmation();
    }

    private void PackageName_OnTextChanged(object? sender, TextChangedEventArgs e) => UpdateConfirmation();

    private void UpdateConfirmation()
    {
        if (ConfirmUninstall is null) return;
        if (_localApp is not null)
            UninstallDetails.Text = _removeOnly ? "Removes this entry from GHD and deletes its saved installer versions. Installed app files and the source file you originally imported are kept."
                : new LocalAppManager().GetUninstallDescription(_localApp);
        else if (_repo is not null)
            UninstallDetails.Text = _installations.GetUninstallDescription(_repo, PackageName.Text);
        ConfirmUninstall.IsEnabled = _removeOnly || _repo is not null && (_localApp?.CanUninstall ?? _repo.CanUninstall)
            && (!_repo.NeedsDebianPackageName || InstallationManager.IsValidDebianPackageName(PackageName.Text?.Trim()));
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Uninstall_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ConfirmUninstall.IsEnabled) Close(PackageName.Text?.Trim() ?? string.Empty);
    }
}
