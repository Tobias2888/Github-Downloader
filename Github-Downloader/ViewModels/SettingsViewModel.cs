using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Github_Downloader.Models;
using Github_Downloader.ViewModels;
using Github_Downloader_lib;
using LoggerLib;

namespace Github_Downloader.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly MainViewModel _mainViewModel;
    private AppSettings _settings;

    public SettingsViewModel()
    {
        _mainViewModel = ((App)Application.Current!).MainViewModel;
        _settings = _mainViewModel.AppSettings;
    }

    public WindowState WindowState
    {
        get => _settings.WindowState;
        set
        {
            _settings.WindowState = value;
            OnPropertyChanged();
            _settings.Save();
        }
    }

    public bool AutoCheckForUpdates
    {
        get => _settings.AutoCheckForUpdates;
        set
        {
            _settings.AutoCheckForUpdates = value;
            OnPropertyChanged();
            _settings.Save();
            
            if (value)
                _mainViewModel.AutoCheckForUpdatesTimer.Start();
            else
                _mainViewModel.AutoCheckForUpdatesTimer.Stop();
        }
    }

    public int CheckForUpdatesInterval
    {
        get => _settings.CheckForUpdatesInterval;
        set
        {
            _settings.CheckForUpdatesInterval = value;
            OnPropertyChanged();
            _settings.Save();
            _mainViewModel.AutoCheckForUpdatesTimer.Interval = TimeSpan.FromMinutes(value);
        }
    }

    public string GlobalDownloadPath
    {
        get => _settings.GlobalDownloadPath;
        set
        {
            _settings.GlobalDownloadPath = value;
            OnPropertyChanged();
            _settings.Save();
        }
    }

    public void SetWindowState(WindowState state)
    {
        WindowState = state;
    }

    public async Task BrowseDownloadPath(Visual visual)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel is null) return;

        IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select Download Folder",
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            GlobalDownloadPath = folders[0].Path.LocalPath;
        }
    }

    public async Task ExportConfig(Visual visual)
    {
        Logger.LogI("Exporting repo.json");
        
        TopLevel? topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel is null) return;
        
        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export Repository Configuration",
                SuggestedFileName = "repos.json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON files")
                    {
                        Patterns = new[] { "*.json" }
                    }
                }
            });

        if (file is not null)
        {
            FileManager.ExportRepoConfig(file.Path.LocalPath);
        }
    }

    public async Task ImportConfig(Visual visual)
    {
        Logger.LogI("Importing repo.json");

        TopLevel? topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel is null) return;

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select repo.json",
                AllowMultiple = false,
            });

        if (files.Count > 0)
        {
            FileManager.ImportRepoConfig(files[0].Path.LocalPath);
        }
    }
}