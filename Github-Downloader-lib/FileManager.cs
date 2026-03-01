using System.Text.Json;
using FileLib;
using Github_Downloader_lib.Models;
using Github_Downloader.Enums;
using LoggerLib;

namespace Github_Downloader_lib;

public static class FileManager
{
    private static readonly string AppdataPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader");
    private static readonly string ReposConfigFilePath = Path.Join(AppdataPath, "repos.json");
    private static readonly string PatFilePath = Path.Join(AppdataPath, "pat");
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

    public static async Task LoadRepos(Action<string> statusText)
    {
        DirectoryHelper.CreateDir(CachePath);
        DirectoryHelper.CreateDir(AppImagesPath);
        
        if (File.Exists(ReposConfigFilePath))
        {
            string jsonString = File.ReadAllText(ReposConfigFilePath);
            UpdateManager.Repos = JsonSerializer.Deserialize<List<Repo>>(jsonString);
        }

        UpdateManager.Repos ??= [];

        if (UpdateManager.CurPlatform != Platform.Avalonia)
        {
            return;
        }
        
        await UpdateManager.UpdateRepoDetails(UpdateManager.Repos);
        await UpdateManager.SearchForUpdates(UpdateManager.Repos, statusText);
        SaveRepos();
    }

    public static void SetPat(string pat)
    {
        if (string.IsNullOrEmpty(pat))
        {
            return;
        }
        File.WriteAllText(PatFilePath, pat);
    }
    
    public static string GetPat()
    {
        return !File.Exists(PatFilePath) ? "" : File.ReadAllText(PatFilePath);
    }
}