using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Github_Downloader.Enums;
using Github_Downloader.ViewModels;

namespace Github_Downloader.Views;

public partial class RepoDetailsView : UserControl
{
    private readonly MainViewModel _mainViewModel;
    private readonly RepoDetailsViewModel _repoDetailsViewModel;
    
    public RepoDetailsView()
    {
        InitializeComponent();
        _mainViewModel = ((App)Application.Current!).MainViewModel;
        _repoDetailsViewModel = ((App)Application.Current!).RepoDetailsViewModel;
        DataContext = _repoDetailsViewModel;
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        LblDownloadPath.DataContext = _repoDetailsViewModel.Repo;
        LblDownloadPath.Bind(
            ContentProperty,
            new MultiBinding
            {
                StringFormat = "Download Path: {0}",
                Bindings =
                {
                    new Binding(nameof(_repoDetailsViewModel.Repo.DownloadPath))
                }
            });

        TbxRepoName.DataContext = _repoDetailsViewModel.Repo;
        TbxRepoName.Bind(TextBox.TextProperty, new Binding(nameof(_repoDetailsViewModel.Repo.Name)));
        
        TbxDescription.DataContext = _repoDetailsViewModel.Repo;
        TbxDescription.Bind(TextBox.TextProperty, new Binding(nameof(_repoDetailsViewModel.Repo.Description)));

        if (!Design.IsDesignMode)
        {
            TbxVersion.Text = "Version: " + (_repoDetailsViewModel.Repo.CurrentInstallTag == _repoDetailsViewModel.Repo.Tag
                ? _repoDetailsViewModel.Repo.CurrentInstallTag
                : $"{_repoDetailsViewModel.Repo.CurrentInstallTag} -> {_repoDetailsViewModel.Repo.Tag}");
        }

        TbxGithubLink.DataContext = _repoDetailsViewModel.Repo;
        TbxGithubLink.Bind(TextBlock.TextProperty, new Binding(nameof(_repoDetailsViewModel.Repo.GitHubLink)));
        
        TbxChangelog.DataContext = _repoDetailsViewModel.Repo;
        TbxChangelog.Bind(TextBlock.TextProperty, new Binding(nameof(_repoDetailsViewModel.Repo.LatestChangelog)));
    }

    private void BtnBack_OnClick(object? sender, RoutedEventArgs e)
    {
        _mainViewModel.SwitchPage(ViewNames.Home);
    }
    
    private async void BtnFilePicker_OnClick(object? sender, RoutedEventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select folder",
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            if (_repoDetailsViewModel.Repo == null)
            {
                return;
            }
            string path = folders[0].Path.LocalPath;
            _repoDetailsViewModel.Repo.DownloadPath = path;
            FileManager.SaveRepos(((App)Application.Current!).Repos);
        }
    }

    private void TbxGithubLink_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = ((TextBlock)sender).Text,
            UseShellExecute = true
        });
    }

    private async void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        List<Repo> repos = ((App)Application.Current!).Repos;
        await UpdateManager.SearchForUpdates(repos);
        await UpdateManager.UpdateRepo(_repoDetailsViewModel.Repo);
        FileManager.SaveRepos(repos);
    }
}