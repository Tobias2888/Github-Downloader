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

    public bool NotificationsEnabled
    {
        get => _settings.Notifications.Enabled;
        set { _settings.Notifications.Enabled = value; OnPropertyChanged(); _settings.Save(); }
    }

    public bool NotifyUpdatesAvailable
    {
        get => _settings.Notifications.UpdatesAvailable;
        set { _settings.Notifications.UpdatesAvailable = value; OnPropertyChanged(); _settings.Save(); }
    }

    public bool NotifyDownloadsCompleted
    {
        get => _settings.Notifications.DownloadsCompleted;
        set { _settings.Notifications.DownloadsCompleted = value; OnPropertyChanged(); _settings.Save(); }
    }

    public bool LimitNotificationTimes
    {
        get => _settings.Notifications.LimitToTimeWindow;
        set { _settings.Notifications.LimitToTimeWindow = value; OnPropertyChanged(); _settings.Save(); }
    }

    public TimeSpan? NotificationStartTime
    {
        get => _settings.Notifications.StartTime;
        set
        {
            if (!value.HasValue) return;
            _settings.Notifications.StartTime = value.Value;
            OnPropertyChanged();
            _settings.Save();
        }
    }

    public TimeSpan? NotificationEndTime
    {
        get => _settings.Notifications.EndTime;
        set
        {
            if (!value.HasValue) return;
            _settings.Notifications.EndTime = value.Value;
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
