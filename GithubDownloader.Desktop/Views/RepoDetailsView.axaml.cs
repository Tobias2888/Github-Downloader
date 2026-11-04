using GithubDownloader.Core.LocalApplications;
using GithubDownloader.Core.Installation.Archives;
using GithubDownloader.Core.Installation;
using GithubDownloader.Core.Configuration;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using GithubDownloader.Core;
using GithubDownloader.Core.Models;
using GithubDownloader.Desktop.Enums;
using GithubDownloader.Desktop.ViewModels;
using LoggerLib;
using ColorTextBlock.Avalonia;

namespace GithubDownloader.Desktop.Views;

public partial class RepoDetailsView : UserControl
{
    private bool _refreshingVersion;
    private readonly MainViewModel _mainViewModel;
    private readonly RepoDetailsViewModel _repoDetailsViewModel;
    private readonly DownloadStatusViewModel _downloadStatusViewModel;
    private Repo? _observedRepo;

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
        if (_repoDetailsViewModel.Repo is not Repo repo) return;
        if (Design.IsDesignMode) return;
        
        if (_observedRepo is not null) _observedRepo.PropertyChanged -= Repo_OnPropertyChanged;
        _observedRepo = repo;
        if (_observedRepo is not null) _observedRepo.PropertyChanged += Repo_OnPropertyChanged;
        UpdateDownloadSettingsVisibility();
        
        TbxDownloadPath.DataContext = repo;
        TbxDownloadPath.Bind(
            TextBox.TextProperty,
            new Binding(nameof(repo.DownloadPath)));

        TbxRepoName.DataContext = repo;
        TbxRepoName.Bind(TextBlock.TextProperty, new Binding(nameof(repo.Name)));
        
        TbxDescription.DataContext = repo;
        TbxDescription.Bind(TextBlock.TextProperty, new Binding(nameof(repo.Description)));

        CobReleaseChannel.SelectedIndex = repo.IncludePrereleases ? 1 : 0;
        CobReleaseChannel.SelectionChanged += async (o, args) =>
        {
            repo.IncludePrereleases = CobReleaseChannel.SelectedIndex == 1;
            await RefreshReleaseAsync();
        };

        CobVersion.ItemsSource = repo.Tags;
        int targetTagIndex = repo.Tags.IndexOf(repo.TargetTag);
        CobVersion.SelectedIndex = targetTagIndex >= 0
            ? targetTagIndex
            : (repo.Tags.Count > 0 ? 0 : -1);
        CobVersion.SelectionChanged += async (o, args) =>
        {
            if (_refreshingVersion) return;
            int selectedIndex = CobVersion.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= repo.Tags.Count) return;
            string tag = repo.Tags[selectedIndex];
            if (tag == repo.TargetTag) return;
            repo.TargetTag = tag;
            
            await RefreshReleaseAsync();
        };
        
        TbxVersion.DataContext = repo;
        TbxVersion.Bind(TextBlock.TextProperty, new Binding(nameof(repo.CurrentVersion)));
        TbxVersion.Bind(IsVisibleProperty, new Binding(nameof(repo.IsUpToDate)));
        
        TbxUpdateVersion.DataContext = repo;
        TbxUpdateVersion.Bind(
            TextBlock.TextProperty,
            new MultiBinding
            {
                StringFormat = "{0} -> {1}",
                Bindings =
                {
                    new Binding(nameof(repo.CurrentVersion)),
                    new Binding(nameof(repo.Tag)),
                }
            });
        TbxUpdateVersion.Bind(IsVisibleProperty, new Binding("!" + nameof(repo.IsUpToDate)));

        TbxGithubLink.DataContext = repo;
        TbxGithubLink.Bind(TextBlock.TextProperty, new Binding(nameof(repo.GitHubLink)));
        
        PopulateChangelog(repo.LatestChangelog);
        
        TbxReleaseDate.DataContext = repo;
        TbxReleaseDate.Bind(
            TextBlock.TextProperty,
            new Binding(nameof(repo.ReleaseDate))
            {
                Converter = new ReleaseDateStringToFormattedConverter()
            });

        TglRenameFile.IsChecked = repo.NewFileName != "";
        TbxRenameFile.IsVisible = TglRenameFile.IsChecked == true;
        TbxRenameFile.Text = repo.NewFileName;
    }

    private void Control_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_observedRepo is not null) _observedRepo.PropertyChanged -= Repo_OnPropertyChanged;
        _observedRepo = null;
    }

    private void Repo_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_repoDetailsViewModel.Repo is not Repo repo) return;
        if (e.PropertyName is nameof(Repo.SelectedAssetName) or nameof(Repo.SaveFileAnyway))
            UpdateDownloadSettingsVisibility();
        if (e.PropertyName == nameof(Repo.LatestChangelog))
            PopulateChangelog(repo.LatestChangelog);
    }

    private void UpdateDownloadSettingsVisibility()
    {
        Repo? repo = _repoDetailsViewModel.Repo;
        if (repo is null) return;

        string extension = Path.GetExtension(repo.SelectedAssetName).ToLowerInvariant();
        BtnDesktopEntryArguments.IsVisible = OperatingSystem.IsLinux()
            && (extension == ".appimage" || repo.InstalledAppImageId.Length > 0
                || ArchiveExtractor.IsSupportedArchive(repo.SelectedAssetName) || repo.ArchiveInstallation is not null);
        StpSaveFileAnyway.IsVisible = UpdateManager.IsInstallerAsset(repo.SelectedAssetName);
        bool installsLinuxPackage = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && UpdateManager.IsInstallerAsset(repo.SelectedAssetName)
            && (extension is ".deb" or ".appimage" || InstallationManager.IsArchPackageFile(repo.SelectedAssetName));
        StpDownloadPath.IsVisible = !installsLinuxPackage || repo.SaveFileAnyway;
    }
    private async void Arguments_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_repoDetailsViewModel.Repo is not Repo repo || TopLevel.GetTopLevel(this) is not Window owner) return;
        App application = (App)Application.Current!;
        if (application.DownloadStatusViewModel.IsUpdating) { application.DownloadStatusViewModel.Show(); return; }
        DesktopEntryArgumentsDialog dialog = new(repo.DesktopEntryArguments, async arguments =>
        {
            if (application.DownloadStatusViewModel.IsUpdating) throw new InvalidOperationException("Wait for the current operation to finish.");
            await new LocalAppManager().SetDesktopEntryArgumentsAsync(repo, arguments, _ => { });
            FileManager.SaveRepos();
        });
        await dialog.ShowDialog(owner);
    }

    private async Task RefreshReleaseAsync()
    {
        if (_repoDetailsViewModel.Repo is not Repo repo) return;
        await _downloadStatusViewModel.RunAsync(() =>
            UpdateManager.SearchForUpdates(repo, _downloadStatusViewModel.LogStatus), showNotification: false);
        _refreshingVersion = true;
        try
        {
            CobVersion.ItemsSource = repo.Tags;
            CobVersion.SelectedItem = repo.TargetTag;
        }
        finally { _refreshingVersion = false; }
        _mainViewModel.HasUpdates = UpdateManager.Repos.Any(repo => repo.HasRelease && !repo.IsUpToDate);
        FileManager.SaveRepos();
        await ((App)Application.Current!).Notifications.NotifyUpdatesAvailableAsync(UpdateManager.Repos);
    }

    private void PopulateChangelog(string? text)
    {
        if (_repoDetailsViewModel.Repo is not Repo repo) return;
        if (string.IsNullOrWhiteSpace(text))
        {
            MarkdownChangelog.Markdown = "No changelog available for this release.";
            return;
        }

        MarkdownChangelog.Markdown = ChangelogMarkdown.Prepare(text);
        string baseUrl = repo.GitHubLink.TrimEnd('/');
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

        _ = Logger.LogI("Opening repository link in browser");
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
        _ = Logger.LogI("Repository download folder selection requested");
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
            _ = Logger.LogI("Repository download folder selection cancelled");
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
        if (sender is TextBlock { Text: string url }) OpenUrl(url);
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
        if (_repoDetailsViewModel.Repo is not Repo repo) return;
        await ((App)Application.Current!).UpdateRepoAsync(repo, reinstall, true);
        FileManager.SaveRepos();
    }

    private async void BtnUninstall_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_repoDetailsViewModel.Repo is Repo repo)
            await ((App)Application.Current!).UninstallRepoAsync(repo);
    }

    private void TglRenameFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_repoDetailsViewModel.Repo is not Repo repo) return;
        _ = Logger.LogI("Repository file-renaming option changed");
        bool rename = TglRenameFile.IsChecked == true;
        TbxRenameFile.IsVisible = rename;
        if (!rename)
        {
            repo.NewFileName = "";
            FileManager.SaveRepos();
        }
    }

    private void TbxRenameFile_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_repoDetailsViewModel.Repo is not Repo repo) return;
        repo.NewFileName = TbxRenameFile.Text ?? "";
        FileManager.SaveRepos();
    }
}
