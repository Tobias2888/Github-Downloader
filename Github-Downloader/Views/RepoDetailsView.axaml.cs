using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Converters;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Github_Downloader.Enums;
using Github_Downloader.ViewModels;
using LoggerLib;

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
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && 
            !_repoDetailsViewModel.Repo.SaveFileAnyway &&
            (_repoDetailsViewModel.Repo.AssetNames[_repoDetailsViewModel.Repo.DownloadAssetIndex].EndsWith(".deb") ||
             _repoDetailsViewModel.Repo.AssetNames[_repoDetailsViewModel.Repo.DownloadAssetIndex].EndsWith(".AppImage")))
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
        CobVersion.SelectedIndex = _repoDetailsViewModel.Repo.Tags.IndexOf(_repoDetailsViewModel.Repo.TargetTag);
        CobVersion.SelectionChanged += async (o, args) =>
        {
            _repoDetailsViewModel.Repo.TargetTag = _repoDetailsViewModel.Repo.Tags[CobVersion.SelectedIndex];
            
            _downloadStatusViewModel.IsUpdating = true;
            await UpdateManager.SearchForUpdates(_repoDetailsViewModel.Repo, statusText =>
            {
                _downloadStatusViewModel.StatusText = statusText;
            });
            _downloadStatusViewModel.IsUpdating = false;
            
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
        if (!(_repoDetailsViewModel.Repo.AssetNames[_repoDetailsViewModel.Repo.DownloadAssetIndex].EndsWith(".deb") ||
              _repoDetailsViewModel.Repo.AssetNames[_repoDetailsViewModel.Repo.DownloadAssetIndex].EndsWith(".AppImage") ||
              _repoDetailsViewModel.Repo.AssetNames[_repoDetailsViewModel.Repo.DownloadAssetIndex].EndsWith(".exe") ||
              _repoDetailsViewModel.Repo.AssetNames[_repoDetailsViewModel.Repo.DownloadAssetIndex].EndsWith(".msi")))
        {
            StpSaveFileAnyway.IsVisible = false;
        }

        TglRenameFile.IsChecked = _repoDetailsViewModel.Repo.NewFileName != ""; 
        TbxRenameFile.IsVisible = TglRenameFile.IsChecked == true;
        TbxRenameFile.Text = _repoDetailsViewModel.Repo.NewFileName;
    }

    private void PopulateChangelog(string? text)
    {
        TbxChangelog.Inlines?.Clear();
        if (string.IsNullOrEmpty(text)) return;

        string baseUrl = _repoDetailsViewModel.Repo.GitHubLink;
        if (baseUrl.EndsWith("/")) baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);
        
        var regex = new Regex(@"#(\d+)");
        int lastIndex = 0;

        foreach (Match match in regex.Matches(text))
        {
            if (match.Index > lastIndex)
            {
                TbxChangelog.Inlines?.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));
            }

            string issueNumber = match.Groups[1].Value;
            string issueUrl = $"{baseUrl}/issues/{issueNumber}";
            
            var link = new InlineUIContainer
            {
                Child = new TextBlock
                {
                    Text = match.Value,
                    Foreground = new SolidColorBrush(new Color(255, 0, 158, 164)),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    TextDecorations = TextDecorations.Underline,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin =  new Thickness(0),
                }
            };
            link.Child.PointerPressed += (s, e) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = issueUrl,
                    UseShellExecute = true
                });
            };
            
            TbxChangelog.Inlines?.Add(link);
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            TbxChangelog.Inlines?.Add(new Run(text.Substring(lastIndex)));
        }
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

        if (folders.Count <= 0) return;
        
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
        Process.Start(new ProcessStartInfo
        {
            FileName = ((TextBlock)sender).Text,
            UseShellExecute = true
        });
    }

    private async void BtnUpdate_OnClick(object? sender, RoutedEventArgs e)
    {
        _downloadStatusViewModel.IsUpdating = true;
        await UpdateManager.SearchForUpdates(_repoDetailsViewModel.Repo, statusText =>
        {
            _downloadStatusViewModel.StatusText = statusText;
        });
        await UpdateManager.UpdateRepo(_repoDetailsViewModel.Repo, statusText =>
        {
            _downloadStatusViewModel.StatusText = statusText;
        }, progressText =>
        {
            _downloadStatusViewModel.ProgressText = progressText;
        });
        _downloadStatusViewModel.IsUpdating = false;
        FileManager.SaveRepos();
    }

    private async void BtnReinstall_OnClick(object? sender, RoutedEventArgs e)
    {
        _downloadStatusViewModel.IsUpdating = true;
        await UpdateManager.SearchForUpdates(_repoDetailsViewModel.Repo, statusText =>
        {
            _downloadStatusViewModel.StatusText = statusText;
        });
        await UpdateManager.UpdateRepo(_repoDetailsViewModel.Repo, statusText =>
        {
            _downloadStatusViewModel.StatusText = statusText;
        }, progressText =>
        {
            _downloadStatusViewModel.ProgressText = progressText;
        }, true);
        _downloadStatusViewModel.IsUpdating = false;
        FileManager.SaveRepos();
    }

    private void TglSaveFileAnyway_OnClick(object? sender, RoutedEventArgs e)
    {
        _repoDetailsViewModel.Repo.SaveFileAnyway = TglSaveFileAnyway.IsChecked == true;
        StpDownloadPath.IsVisible = _repoDetailsViewModel.Repo.SaveFileAnyway;
    }

    private void TglRenameFile_OnClick(object? sender, RoutedEventArgs e)
    {
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
        Logger.LogI("Changed 'new file name'");

        _repoDetailsViewModel.Repo.NewFileName = TbxRenameFile.Text;
        FileManager.SaveRepos();
    }
}