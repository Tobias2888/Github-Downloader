using System;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using FileLib;
using Github_Downloader_lib;
using LoggerLib;

namespace Github_Downloader.Models;

public class AppSettings
{
    private static string _appSettingsFilePath = Path.Join(FileManager.AppdataPath, "appsettings.json");
    
    public WindowState WindowState { get; set; } = WindowState.Maximized;
    public bool AutoCheckForUpdates { get; set; } = true;
    public int CheckForUpdatesInterval { get; set; } = 5;
    public string GlobalDownloadPath { get; set; } = DirectoryHelper.GetUserDirPath();
    public string DefaultAssetRule { get; set; } = AssetSelection.GetDefaultRule();
    public NotificationSettings Notifications { get; set; } = new();
    
    public static AppSettings Load()
    {
        Logger.LogI("Loading application settings");
        try
        {
            if (File.Exists(_appSettingsFilePath))
            {
                string jsonString = File.ReadAllText(_appSettingsFilePath);
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(jsonString) ?? new();
                settings.DefaultAssetRule ??= AssetSelection.GetDefaultRule();
                settings.Notifications ??= new();
                Logger.LogI("Application settings loaded");
                return settings;
            }

            Logger.LogI("Using default application settings");
            return new();
        }
        catch (Exception exception)
        {
            Logger.LogE($"Application settings load failed ({exception.GetType().Name})");
            throw;
        }
    }

    public void Save()
    {
        Logger.LogD("Saving application settings");
        try
        {
            string jsonString = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            Directory.CreateDirectory(FileManager.AppdataPath);
            File.WriteAllText(_appSettingsFilePath, jsonString);
            Logger.LogD("Application settings saved");
        }
        catch (Exception exception)
        {
            Logger.LogE($"Application settings save failed ({exception.GetType().Name})");
            throw;
        }
    }
}
