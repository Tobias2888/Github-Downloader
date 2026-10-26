using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using Github_Downloader.Views;
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
    private int _localInstallerRetentionMode;
    private int _localInstallerVersionsToKeep;
    private string _localInstallerRetentionStatus = "";
    private bool _showLocalInstallerRetentionConfirmation;

    public SettingsViewModel()
    {
        _mainViewModel = ((App)Application.Current!).MainViewModel;
        _settings = _mainViewModel.AppSettings;
        _localInstallerRetentionMode = _settings.LocalInstallerVersionsToKeep.HasValue ? 0 : 1;
        _localInstallerVersionsToKeep = _settings.LocalInstallerVersionsToKeep ?? LocalInstallerRetention.DefaultVersionsToKeep;
    }

    public int LocalInstallerRetentionMode
    {
        get => _localInstallerRetentionMode;
        set
        {
            if (value is < 0 or > 1 || value == _localInstallerRetentionMode) return;
            _localInstallerRetentionMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LimitLocalInstallerVersions));
            OnPropertyChanged(nameof(IsReducingLocalInstallerRetention));
            ShowLocalInstallerRetentionConfirmation = false;
            LocalInstallerRetentionStatus = "";
        }
    }

    public bool LimitLocalInstallerVersions => LocalInstallerRetentionMode == 0;

    public int LocalInstallerVersionsToKeep
    {
        get => _localInstallerVersionsToKeep;
        set
        {
            if (value < 0 || value == _localInstallerVersionsToKeep) return;
            _localInstallerVersionsToKeep = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReducingLocalInstallerRetention));
            ShowLocalInstallerRetentionConfirmation = false;
            LocalInstallerRetentionStatus = "";
        }
    }

    public string LocalInstallerRetentionStatus
    {
        get => _localInstallerRetentionStatus;
        private set { _localInstallerRetentionStatus = value; OnPropertyChanged(); }
    }

    public bool IsReducingLocalInstallerRetention => LimitLocalInstallerVersions
        && (!_settings.LocalInstallerVersionsToKeep.HasValue || LocalInstallerVersionsToKeep < _settings.LocalInstallerVersionsToKeep.Value);

    public bool ShowLocalInstallerRetentionConfirmation
    {
        get => _showLocalInstallerRetentionConfirmation;
        private set { _showLocalInstallerRetentionConfirmation = value; OnPropertyChanged(); }
    }

    public void CancelLocalInstallerRetention() => ShowLocalInstallerRetentionConfirmation = false;

    public void ConfirmLocalInstallerRetention()
    {
        if (ShowLocalInstallerRetentionConfirmation) SaveLocalInstallerRetention(confirmed: true);
    }

    public void SaveLocalInstallerRetention() => SaveLocalInstallerRetention(confirmed: false);

    private void SaveLocalInstallerRetention(bool confirmed)
    {
        if (((App)Application.Current!).DownloadStatusViewModel.IsUpdating)
        {
            LocalInstallerRetentionStatus = "Wait for the current operation to finish, then save the limit.";
            return;
        }
        if (IsReducingLocalInstallerRetention && !confirmed)
        {
            ShowLocalInstallerRetentionConfirmation = true;
            return;
        }
        ShowLocalInstallerRetentionConfirmation = false;
        try
        {
            _settings.LocalInstallerVersionsToKeep = LimitLocalInstallerVersions ? LocalInstallerVersionsToKeep : null;
            _settings.Save();
            OnPropertyChanged(nameof(IsReducingLocalInstallerRetention));
            LocalApps.Store.ApplyInstallerRetention(_settings.LocalInstallerVersionsToKeep);
            LocalInstallerRetentionStatus = "Saved. The limit applies to existing and future installers.";
            Logger.LogI("Setting changed: LocalInstallerVersionsToKeep");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LocalInstallerRetentionStatus = "Could not apply the limit to all saved installers. Check file access and try again.";
            Logger.LogE($"Local installer retention failed ({exception.GetType().Name})");
        }
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

    private string _configurationStatus = "";
    public string ConfigurationStatus
    {
        get => _configurationStatus;
        private set { _configurationStatus = value; OnPropertyChanged(); }
    }

    private static readonly FilePickerFileType[] ArchiveFileTypes =
    [
        new("ZIP archive") { Patterns = ["*.zip"] },
        new("Gzip compressed TAR archive") { Patterns = ["*.tar.gz", "*.tag.gz"] }
    ];

    public async Task ExportConfig(Visual visual)
    {
        Logger.LogI("Configuration export requested");
        if (TopLevel.GetTopLevel(visual) is not Window owner) return;
        ConfigurationStatus = "";
        InstallerExportMode? mode = await new ExportConfigurationDialog().ShowDialog<InstallerExportMode?>(owner);
        if (mode is null) return;
        try
        {
            IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Configuration",
                SuggestedFileName = "github-downloader-config.zip",
                DefaultExtension = "zip",
                ShowOverwritePrompt = true,
                FileTypeChoices = ArchiveFileTypes
            });
            if (file is null) return;
            _settings.Save();
            FileManager.SaveRepos();
            await Task.Run(() => ConfigurationArchive.Export(file.Path.LocalPath, mode.Value, FileManager.AppdataPath));
            ConfigurationStatus = "Configuration exported.";
            Logger.LogI("Configuration export completed");
        }
        catch (Exception exception)
        {
            ConfigurationStatus = $"Export failed: {exception.Message}";
            Logger.LogE($"Configuration export failed ({exception.GetType().Name})");
        }
    }

    public async Task ImportConfig(Visual visual)
    {
        Logger.LogI("Configuration import requested");
        TopLevel? topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel is null) return;
        ConfigurationStatus = "";
        if (((App)Application.Current!).DownloadStatusViewModel.IsUpdating)
        {
            ConfigurationStatus = "Wait for the current operation to finish before importing configuration.";
            return;
        }
        try
        {
            IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Configuration",
                AllowMultiple = false,
                FileTypeFilter = [new("Configuration files") { Patterns = ["*.zip", "*.tar.gz", "*.tag.gz", "*.json"] }]
            });
            if (files.Count == 0) return;
            string path = files[0].Path.LocalPath;
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                await FileManager.ImportRepoConfig(path);
            else
            {
                await Task.Run(() => ConfigurationArchive.Import(path, FileManager.AppdataPath, json =>
                {
                    AppSettings imported = JsonSerializer.Deserialize<AppSettings>(json)
                        ?? throw new InvalidDataException("Invalid application settings.");
                    if (imported.CheckForUpdatesInterval <= 0)
                        throw new InvalidDataException("Invalid update interval.");
                    if (imported.LocalInstallerVersionsToKeep < 0)
                        throw new InvalidDataException("Invalid installer retention setting.");
                }));
                await FileManager.LoadRepos(refreshDetails: false);
                _settings = AppSettings.Load();
                _mainViewModel.AppSettings = _settings;
                _mainViewModel.AutoCheckForUpdatesTimer.Interval = TimeSpan.FromMinutes(_settings.CheckForUpdatesInterval);
                if (_settings.AutoCheckForUpdates) _mainViewModel.AutoCheckForUpdatesTimer.Start();
                else _mainViewModel.AutoCheckForUpdatesTimer.Stop();
                ShowLocalInstallerRetentionConfirmation = false;
                LocalInstallerRetentionStatus = "";
                _localInstallerRetentionMode = _settings.LocalInstallerVersionsToKeep.HasValue ? 0 : 1;
                _localInstallerVersionsToKeep = _settings.LocalInstallerVersionsToKeep ?? LocalInstallerRetention.DefaultVersionsToKeep;
                OnPropertyChanged(string.Empty);
            }
            ConfigurationStatus = "Configuration imported.";
            Logger.LogI("Configuration import completed");
        }
        catch (Exception exception)
        {
            ConfigurationStatus = $"Import failed: {exception.Message}";
            Logger.LogE($"Configuration import failed ({exception.GetType().Name})");
        }
    }
}
