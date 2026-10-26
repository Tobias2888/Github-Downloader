using System.Collections.ObjectModel;
using System.Text.Json;
using FileLib;
using Github_Downloader_lib.Models;
using Github_Downloader.Enums;
using LoggerLib;

namespace Github_Downloader_lib;

public static class FileManager
{
    private static readonly string? DataDirectoryOverride = Environment.GetEnvironmentVariable("GITHUB_DOWNLOADER_DATA_DIR");
    public static readonly string AppdataPath = string.IsNullOrWhiteSpace(DataDirectoryOverride)
        ? Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader")
        : Path.GetFullPath(DataDirectoryOverride);
    public static readonly string ReposConfigFilePath = Path.Join(AppdataPath, "repos.json");
    public static readonly string CachePath = string.IsNullOrWhiteSpace(DataDirectoryOverride)
        ? Path.Join(DirectoryHelper.GetCacheDirPath(), "github-downloader")
        : Path.Join(AppdataPath, "cache");
    public static readonly string AppImagesPath = Path.Join(AppdataPath, "app-images");

    public static void SaveRepos()
    {
        Logger.LogD("Repository configuration save started");
        try
        {
            if (!File.Exists(ReposConfigFilePath))
            {
                FileHelper.Create(ReposConfigFilePath);
            }

            string jsonString = JsonSerializer.Serialize(UpdateManager.Repos, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(ReposConfigFilePath, jsonString);
            Logger.LogD("Repository configuration save completed");
        }
        catch (Exception exception)
        {
            Logger.LogE($"Repository configuration save failed ({exception.GetType().Name})");
            throw;
        }
    }

    public static async Task LoadRepos(bool refreshDetails = true)
    {
        Logger.LogI("Repository configuration load started");
        try
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

            if (!refreshDetails || UpdateManager.CurPlatform != Platform.Avalonia)
            {
                Logger.LogI("Repository configuration loaded");
                return;
            }

            await UpdateManager.UpdateRepoDetails(UpdateManager.Repos);
            SaveRepos();
            Logger.LogI("Repository configuration loaded");
        }
        catch (Exception exception)
        {
            Logger.LogE($"Repository configuration load failed ({exception.GetType().Name})");
            throw;
        }
    }

    private static void ClearCache()
    {
        Logger.LogD("Clearing cache");

        foreach (string file in Directory.GetFiles(CachePath))
        {
            File.Delete(file);
        }

        foreach (string dir in Directory.GetDirectories(CachePath))
        {
            Directory.Delete(dir, true);
        }
        Logger.LogD("Cache cleared");
    }

    public static void ExportRepoConfig(string destFile)
    {
        Logger.LogI("Configuration export started");
        try
        {
            if (!File.Exists(ReposConfigFilePath))
            {
                Logger.LogW("Configuration export skipped: configuration does not exist");
                return;
            }

            File.Copy(ReposConfigFilePath, destFile, true);
            Logger.LogI("Configuration export completed");
        }
        catch (Exception exception)
        {
            Logger.LogE($"Configuration export failed ({exception.GetType().Name})");
            throw;
        }
    }

    public static async Task ImportRepoConfig(string sourceFile)
    {
        Logger.LogI("Configuration import started");
        try
        {
            if (!File.Exists(sourceFile))
            {
                Logger.LogW("Configuration import skipped: source does not exist");
                return;
            }

            File.Copy(sourceFile, Path.Join(AppdataPath, "repos.json"), true);
            await LoadRepos();

            foreach (Repo repo in UpdateManager.Repos)
            {
                repo.CurrentInstallTag = "";
                repo.LastDownloadTag = "";
                repo.InstalledDebianPackage = "";
                repo.InstalledArchPackage = "";
                repo.InstalledAppImageId = "";
            }
            Logger.LogI("Configuration import completed");
        }
        catch (Exception exception)
        {
            Logger.LogE($"Configuration import failed ({exception.GetType().Name})");
            throw;
        }
    }
}
