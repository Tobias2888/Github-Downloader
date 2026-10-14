using System;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using FileLib;
using Github_Downloader_lib;

namespace Github_Downloader.Models;

public class AppSettings
{
    private static string _appSettingsFilePath = Path.Join(FileManager.AppdataPath, "appsettings.json");
    
    public WindowState WindowState { get; set; } = WindowState.Maximized;
    public bool AutoCheckForUpdates { get; set; } = true;
    public int CheckForUpdatesInterval { get; set; } = 5;
    public string GlobalDownloadPath { get; set; } = DirectoryHelper.GetUserDirPath();
    public NotificationSettings Notifications { get; set; } = new();
    
    public static AppSettings Load()
    {
        if (File.Exists(_appSettingsFilePath))
        {
            string jsonString = File.ReadAllText(_appSettingsFilePath);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(jsonString) ?? new();
            settings.Notifications ??= new();
            return settings;
        }

        return new();
    }

    public void Save()
    {
        string jsonString = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        
        File.WriteAllText(_appSettingsFilePath, jsonString);
    }
}
