using GithubDownloader.Core.Repositories;
using GithubDownloader.Core.LocalApplications;
using GithubDownloader.Core.Configuration;
using System;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using FileLib;
using GithubDownloader.Core;
using LoggerLib;

namespace GithubDownloader.Desktop.Models;

public class AppSettings
{
    private static string _appSettingsFilePath = Path.Join(FileManager.AppdataPath, "appsettings.json");
    
    public WindowState WindowState { get; set; } = WindowState.Maximized;
    public bool AutoCheckForUpdates { get; set; } = true;
    public int CheckForUpdatesInterval { get; set; } = 5;
    public string GlobalDownloadPath { get; set; } = DirectoryHelper.GetUserDirPath();
    public string DefaultAssetRule { get; set; } = AssetSelection.GetDefaultRule();
    public int? LocalInstallerVersionsToKeep { get; set; } = LocalInstallerRetention.DefaultVersionsToKeep;
    public NotificationSettings Notifications { get; set; } = new();
    private int _logsToKeep = 30;
    public int LogsToKeep
    {
        get => _logsToKeep;
        set => _logsToKeep = value == -1 || value >= 1 ? value : -1;
    }
    
    public static AppSettings Load(bool writeLogs = true)
    {
        if (writeLogs) Logger.LogI("Loading application settings");
        try
        {
            if (File.Exists(_appSettingsFilePath))
            {
                string jsonString = File.ReadAllText(_appSettingsFilePath);
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(jsonString) ?? new();
                settings.DefaultAssetRule ??= AssetSelection.GetDefaultRule();
                settings.Notifications ??= new();
                if (settings.LocalInstallerVersionsToKeep < 0) settings.LocalInstallerVersionsToKeep = LocalInstallerRetention.DefaultVersionsToKeep;
                if (writeLogs) Logger.LogI("Application settings loaded");
                return settings;
            }

            if (writeLogs) Logger.LogI("Using default application settings");
            return new();
        }
        catch (Exception exception)
        {
            if (writeLogs) Logger.LogE($"Application settings load failed ({exception.GetType().Name})");
            throw;
        }
    }

    public void Save()
    {
        _ = Logger.LogD("Saving application settings");
        try
        {
            string jsonString = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            Directory.CreateDirectory(FileManager.AppdataPath);
            File.WriteAllText(_appSettingsFilePath, jsonString);
            _ = Logger.LogD("Application settings saved");
        }
        catch (Exception exception)
        {
            _ = Logger.LogE($"Application settings save failed ({exception.GetType().Name})");
            throw;
        }
    }
}
