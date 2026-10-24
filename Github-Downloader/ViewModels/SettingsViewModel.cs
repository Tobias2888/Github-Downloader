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
            if (_settings.WindowState == value) return;
            Logger.LogI("Setting changed: WindowState");
            _settings.WindowState = value;
            OnPropertyChanged();
            _settings.Save();
        }
    }

    public bool NotificationsEnabled
    {
        get => _settings.Notifications.Enabled;
        set
        {
            if (_settings.Notifications.Enabled == value) return;
            Logger.LogI("Setting changed: NotificationsEnabled");
            _settings.Notifications.Enabled = value;
            OnPropertyChanged();
            _settings.Save();
        }
    }

    public bool NotifyUpdatesAvailable
    {
        get => _settings.Notifications.UpdatesAvailable;
        set
        {
            if (_settings.Notifications.UpdatesAvailable == value) return;
            Logger.LogI("Setting changed: NotifyUpdatesAvailable");
            _settings.Notifications.UpdatesAvailable = value;
            OnPropertyChanged();
            _settings.Save();
        }
    }

    public int UpdateNotificationMode
    {
        get => _settings.Notifications.RepeatUpdateNotifications ? 1 : 0;
        set
        {
            if (value is < 0 or > 1 || UpdateNotificationMode == value) return;
            _settings.Notifications.RepeatUpdateNotifications = value == 1;
            Logger.LogI("Setting changed: UpdateNotificationMode");
            OnPropertyChanged();
            OnPropertyChanged(nameof(RepeatUpdateNotifications));
            _settings.Save();
        }
    }

    public bool RepeatUpdateNotifications => _settings.Notifications.RepeatUpdateNotifications;

    public int UpdateReminderIntervalMinutes
    {
        get => _settings.Notifications.UpdateReminderIntervalMinutes;
        set
        {
            if (UpdateReminderIntervalMinutes == value) return;
            _settings.Notifications.UpdateReminderIntervalMinutes = value;
            Logger.LogI("Setting changed: UpdateReminderIntervalMinutes");
            OnPropertyChanged();
            _settings.Save();
        }
    }

    public bool NotifyDownloadsCompleted
    {
        get => _settings.Notifications.DownloadsCompleted;
        set
        {
            if (_settings.Notifications.DownloadsCompleted == value) return;
            Logger.LogI("Setting changed: NotifyDownloadsCompleted");
            _settings.Notifications.DownloadsCompleted = value;
            OnPropertyChanged();
            _settings.Save();
        }
    }

    public bool LimitNotificationTimes
    {
        get => _settings.Notifications.LimitToTimeWindow;
        set
        {
            if (_settings.Notifications.LimitToTimeWindow == value) return;
            Logger.LogI("Setting changed: LimitNotificationTimes");
            _settings.Notifications.LimitToTimeWindow = value;
            OnPropertyChanged();
            _settings.Save();
        }
    }

    public TimeSpan? NotificationStartTime
    {
        get => _settings.Notifications.StartTime;
        set
        {
            if (!value.HasValue) return;
            if (_settings.Notifications.StartTime == value.Value) return;
            Logger.LogI("Setting changed: NotificationStartTime");
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
            if (_settings.Notifications.EndTime == value.Value) return;
            Logger.LogI("Setting changed: NotificationEndTime");
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
            if (_settings.AutoCheckForUpdates == value) return;
            Logger.LogI("Setting changed: AutoCheckForUpdates");
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
            if (_settings.CheckForUpdatesInterval == value) return;
            Logger.LogI("Setting changed: CheckForUpdatesInterval");
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
            if (_settings.GlobalDownloadPath == value) return;
            Logger.LogI("Setting changed: GlobalDownloadPath");
            _settings.GlobalDownloadPath = value;
            OnPropertyChanged();
            _settings.Save();
        }
    }

    public string DefaultAssetRule
    {
        get => _settings.DefaultAssetRule;
        set
        {
            string rule = value ?? "";
            if (_settings.DefaultAssetRule == rule) return;
            Logger.LogI("Setting changed: DefaultAssetRule");
            _settings.DefaultAssetRule = rule;
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
        Logger.LogI("Download folder selection requested");
        TopLevel? topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel is null) return;

        IReadOnlyList<IStorageFolder> folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select Download Folder",
                AllowMultiple = false
            });

        if (folders.Count == 0) Logger.LogI("Download folder selection cancelled");
        if (folders.Count > 0)
        {
            GlobalDownloadPath = folders[0].Path.LocalPath;
        }
    }

    public async Task ExportConfig(Visual visual)
    {
        Logger.LogI("Configuration export requested");
        
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

        if (file is null) Logger.LogI("Configuration export cancelled");
        if (file is not null)
        {
            FileManager.ExportRepoConfig(file.Path.LocalPath);
        }
    }

    public async Task ImportConfig(Visual visual)
    {
        Logger.LogI("Configuration import requested");

        TopLevel? topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel is null) return;

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select repo.json",
                AllowMultiple = false,
            });

        if (files.Count == 0) Logger.LogI("Configuration import cancelled");
        if (files.Count > 0)
        {
            await FileManager.ImportRepoConfig(files[0].Path.LocalPath);
        }
    }
}
