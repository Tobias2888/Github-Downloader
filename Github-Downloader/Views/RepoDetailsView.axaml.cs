using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Converters;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Github_Downloader.Enums;
using Github_Downloader.ViewModels;
using LoggerLib;
using ColorTextBlock.Avalonia;

namespace Github_Downloader.Views;

public partial class RepoDetailsView : UserControl
{
    private readonly MainViewModel _mainViewModel;
    private readonly RepoDetailsViewModel _repoDetailsViewModel;
    private readonly DownloadStatusViewModel _downloadStatusViewModel;

    public RepoDetailsView()
    {
        InitializeComponent();
        _mainViewModel = ((App)Application.Current!).MainViewModel;
        _repoDetailsViewModel = ((App)Application.Current!).RepoDetailsViewModel;
        _downloadStatusViewModel = ((App)Application.Current!).DownloadStatusViewModel;
        DataContext = _repoDetailsViewModel;
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (Design.IsDesignMode) return;
        
        string selectedAssetExtension = Path.GetExtension(_repoDetailsViewModel.Repo.SelectedAssetName).ToLowerInvariant();
        bool isLinuxPackage = selectedAssetExtension is ".deb" or ".appimage";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && 
            !_repoDetailsViewModel.Repo.SaveFileAnyway &&
            isLinuxPackage)
        {
            StpDownloadPath.IsVisible = false;
        }
        
        TbxDownloadPath.DataContext = _repoDetailsViewModel.Repo;
        TbxDownloadPath.Bind(
            TextBox.TextProperty,
            new Binding(nameof(_repoDetailsViewModel.Repo.DownloadPath)));

        TbxRepoName.DataContext = _repoDetailsViewModel.Repo;
        TbxRepoName.Bind(TextBlock.TextProperty, new Binding(nameof(_repoDetailsViewModel.Repo.Name)));
        
        TbxDescription.DataContext = _repoDetailsViewModel.Repo;
        TbxDescription.Bind(TextBlock.TextProperty, new Binding(nameof(_repoDetailsViewModel.Repo.Description)));

        CobVersion.ItemsSource = _repoDetailsViewModel.Repo.Tags;
        int targetTagIndex = _repoDetailsViewModel.Repo.Tags.IndexOf(_repoDetailsViewModel.Repo.TargetTag);
        CobVersion.SelectedIndex = targetTagIndex >= 0
            ? targetTagIndex
            : (_repoDetailsViewModel.Repo.Tags.Count > 0 ? 0 : -1);
        CobVersion.SelectionChanged += async (o, args) =>
        {
            int selectedIndex = CobVersion.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _repoDetailsViewModel.Repo.Tags.Count) return;
            _repoDetailsViewModel.Repo.TargetTag = _repoDetailsViewModel.Repo.Tags[selectedIndex];
            
            await _downloadStatusViewModel.RunAsync(() =>
                UpdateManager.SearchForUpdates(_repoDetailsViewModel.Repo, _downloadStatusViewModel.LogStatus));
            
            bool hasUpdates = false;
            foreach (Repo repo in UpdateManager.Repos)
            {
                if (repo.CurrentInstallTag != repo.Tag)
                {
                    hasUpdates = true;
                }
            }
            _mainViewModel.HasUpdates = hasUpdates;
            FileManager.SaveRepos();
            await ((App)Application.Current!).Notifications.NotifyUpdatesAvailableAsync(UpdateManager.Repos);
        };
        
        TbxVersion.DataContext = _repoDetailsViewModel.Repo;
        TbxVersion.Bind(TextBlock.TextProperty, new Binding(nameof(_repoDetailsViewModel.Repo.CurrentInstallTag)));
        TbxVersion.Bind(IsVisibleProperty, new Binding(nameof(_repoDetailsViewModel.Repo.IsUpToDate)));
        
        TbxUpdateVersion.DataContext = _repoDetailsViewModel.Repo;
        TbxUpdateVersion.Bind(
            TextBlock.TextProperty,
            new MultiBinding
            {
                StringFormat = "{0} -> {1}",
                Bindings =
                {
                    new Binding(nameof(_repoDetailsViewModel.Repo.CurrentInstallTag)),
                    new Binding(nameof(_repoDetailsViewModel.Repo.Tag)),
                }
            });
        TbxUpdateVersion.Bind(IsVisibleProperty, new Binding("!" + nameof(_repoDetailsViewModel.Repo.IsUpToDate)));

        TbxGithubLink.DataContext = _repoDetailsViewModel.Repo;
        TbxGithubLink.Bind(TextBlock.TextProperty, new Binding(nameof(_repoDetailsViewModel.Repo.GitHubLink)));
        
        _repoDetailsViewModel.Repo.PropertyChanged += (o, args) =>
        {
            if (args.PropertyName == nameof(Repo.LatestChangelog))
            {
                PopulateChangelog(_repoDetailsViewModel.Repo.LatestChangelog);
            }
        };
        PopulateChangelog(_repoDetailsViewModel.Repo.LatestChangelog);
        
        TbxReleaseDate.DataContext = _repoDetailsViewModel.Repo;
        TbxReleaseDate.Bind(
            TextBlock.TextProperty,
            new Binding(nameof(_repoDetailsViewModel.Repo.ReleaseDate))
            {
                Converter = new ReleaseDateStringToFormattedConverter()
            });

        TglSaveFileAnyway.IsChecked = _repoDetailsViewModel.Repo.SaveFileAnyway;
        if (selectedAssetExtension is not (".deb" or ".appimage" or ".exe" or ".msi"))
        {
            StpSaveFileAnyway.IsVisible = false;
        }

        TglRenameFile.IsChecked = _repoDetailsViewModel.Repo.NewFileName != ""; 
        TbxRenameFile.IsVisible = TglRenameFile.IsChecked == true;
        TbxRenameFile.Text = _repoDetailsViewModel.Repo.NewFileName;
    }
    private void PopulateChangelog(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            MarkdownChangelog.Markdown = "No changelog available for this release.";
            return;
        }

        MarkdownChangelog.Markdown = ChangelogMarkdown.Prepare(text);
        string baseUrl = _repoDetailsViewModel.Repo.GitHubLink.TrimEnd('/');
        foreach (CTextBlock block in MarkdownChangelog.GetLogicalDescendants().OfType<CTextBlock>().ToArray())
        {
            if (block.Classes.Contains("ListMarker"))
            {
                // Markdown.Avalonia chooses a shape from *, - or +. Use nesting like a browser does.
                if (block.Text is "•" or "○" or "▪" or "❏")
                {
                    int depth = block.GetLogicalAncestors().OfType<Grid>()
                        .Count(grid => grid.Classes.Contains("List"));
                    block.Content = new()
                    {
                        new CRun { Text = depth switch { <= 1 => "•", 2 => "○", _ => "▪" } }
                    };
                }
                continue;
            }

            // Add issue links after parsing, preserving Markdown links and code spans.
            CInline[] content = GitHubIssueLinker.Linkify(block.Content, baseUrl, OpenUrl).ToArray();
            block.Content.Clear();
            block.Content.AddRange(content);
        }
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        Logger.LogI("Opening repository link in browser");
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void BtnBack_OnClick(object? sender, RoutedEventArgs e)
    {
        _mainViewModel.SwitchPage(ViewNames.Home);
    }
    
    private async void BtnFilePicker_OnClick(object? sender, RoutedEventArgs e)
    {
        Logger.LogI("Repository download folder selection requested");
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

        if (folders.Count <= 0)
        {
            Logger.LogI("Repository download folder selection cancelled");
            return;
        }
        
        if (_repoDetailsViewModel.Repo == null)
        {
            return;
        }
        string path = folders[0].Path.LocalPath;
        _repoDetailsViewModel.Repo.DownloadPath = path;
        FileManager.SaveRepos();
    }

    private void TbxGithubLink_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        OpenUrl(((TextBlock)sender).Text);
    }

    private async void BtnUpdate_OnClick(object? sender, RoutedEventArgs e)
    {
        await UpdateRepoAsync(false);
    }

    private async void BtnReinstall_OnClick(object? sender, RoutedEventArgs e)
    {
        await UpdateRepoAsync(true);
    }

    private async Task UpdateRepoAsync(bool reinstall)
    {
        await ((App)Application.Current!).UpdateRepoAsync(_repoDetailsViewModel.Repo, reinstall, true);
        FileManager.SaveRepos();
    }

    private async void BtnUninstall_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_repoDetailsViewModel.Repo is Repo repo)
            await ((App)Application.Current!).UninstallRepoAsync(repo);
    }

    private void TglSaveFileAnyway_OnClick(object? sender, RoutedEventArgs e)
    {
        _repoDetailsViewModel.Repo.SaveFileAnyway = TglSaveFileAnyway.IsChecked == true;
        StpDownloadPath.IsVisible = _repoDetailsViewModel.Repo.SaveFileAnyway;
    }

    private void TglRenameFile_OnClick(object? sender, RoutedEventArgs e)
    {
        Logger.LogI("Repository file-renaming option changed");
        bool rename = TglRenameFile.IsChecked == true;
        TbxRenameFile.IsVisible = rename;
        if (!rename)
        {
            _repoDetailsViewModel.Repo.NewFileName = "";
            FileManager.SaveRepos();
        }
    }

    private void TbxRenameFile_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _repoDetailsViewModel.Repo.NewFileName = TbxRenameFile.Text;
        FileManager.SaveRepos();
    }
}
