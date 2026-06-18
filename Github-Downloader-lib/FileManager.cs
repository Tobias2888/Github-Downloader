using System.Collections.ObjectModel;
using System.Text.Json;
using FileLib;
using Github_Downloader_lib.Models;
using Github_Downloader.Enums;
using LoggerLib;

namespace Github_Downloader_lib;

public static class FileManager
{
    public static readonly string AppdataPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader");
    public static readonly string ReposConfigFilePath = Path.Join(AppdataPath, "repos.json");
    public static readonly string CachePath = Path.Join(DirectoryHelper.GetCacheDirPath(), "github-downloader");
    public static readonly string AppImagesPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader", "app-images");
    
    public static void SaveRepos()
    {
        Logger.LogI("Saving repos");
        if (!File.Exists(ReposConfigFilePath))
        {
            FileHelper.Create(ReposConfigFilePath);
        }

        string jsonString = JsonSerializer.Serialize(UpdateManager.Repos, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        
        File.WriteAllText(ReposConfigFilePath, jsonString);
    }

    public static async Task LoadRepos()
    {
        DirectoryHelper.CreateDir(AppdataPath);
        DirectoryHelper.CreateDir(CachePath);
        DirectoryHelper.CreateDir(AppImagesPath);
        
        ClearCache();
        
        if (File.Exists(ReposConfigFilePath))
        {
            string jsonString = await File.ReadAllTextAsync(ReposConfigFilePath);
            UpdateManager.Repos = JsonSerializer.Deserialize<ObservableCollection<Repo>>(jsonString);
        }

        UpdateManager.Repos ??= [];
        UpdateManager.WatchRepos();

        if (UpdateManager.CurPlatform != Platform.Avalonia)
        {
            return;
        }
        
        await UpdateManager.UpdateRepoDetails(UpdateManager.Repos);
        SaveRepos();
    }

    private static void ClearCache()
    {
        Logger.LogI("Clearing cache");
        
        foreach (string file in Directory.GetFiles(CachePath))
        {
            File.Delete(file);
        }

        foreach (string dir in Directory.GetDirectories(CachePath))
        {
            Directory.Delete(dir, true);
        }
    }

    public static void ExportRepoConfig(string destFile)
    {
        if (!File.Exists(ReposConfigFilePath))
        {
            Logger.LogI($"Source file {ReposConfigFilePath} not found");
            return;
        }
        
        File.Copy(ReposConfigFilePath, destFile, true);
    }

    public static void ImportRepoConfig(string sourceFile)
    {
        if (!File.Exists(sourceFile))
        {
            Logger.LogI($"File {sourceFile} not found");
            return;
        }
        
        File.Copy(sourceFile, Path.Join(AppdataPath, "repos.json"), true);
        LoadRepos();

        foreach (Repo repo in UpdateManager.Repos)
        {
            repo.CurrentInstallTag = "";
        }
    }
}