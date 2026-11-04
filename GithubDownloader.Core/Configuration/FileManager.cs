using System.Collections.ObjectModel;
using System.Text.Json;
using FileLib;
using GithubDownloader.Core.Models;
using GithubDownloader.Core.Enums;
using LoggerLib;

namespace GithubDownloader.Core.Configuration;

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
        _ = Logger.LogD("Repository configuration save started");
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
            _ = Logger.LogD("Repository configuration save completed");
        }
        catch (Exception exception)
        {
            _ = Logger.LogE($"Repository configuration save failed ({exception.GetType().Name})");
            throw;
        }
    }

    public static async Task LoadRepos(bool refreshDetails = true)
    {
        _ = Logger.LogI("Repository configuration load started");
        try
        {
            DirectoryHelper.CreateDir(AppdataPath);
            DirectoryHelper.CreateDir(CachePath);
            DirectoryHelper.CreateDir(AppImagesPath);

            ClearCache();

            if (File.Exists(ReposConfigFilePath))
            {
                string jsonString = await File.ReadAllTextAsync(ReposConfigFilePath);
                UpdateManager.Repos = JsonSerializer.Deserialize<ObservableCollection<Repo>>(jsonString) ?? [];
            }

            UpdateManager.Repos ??= [];
            UpdateManager.WatchRepos();

            if (!refreshDetails || UpdateManager.CurPlatform != Platform.Avalonia)
            {
                _ = Logger.LogI("Repository configuration loaded");
                return;
            }

            await UpdateManager.UpdateRepoDetails(UpdateManager.Repos);
            SaveRepos();
            _ = Logger.LogI("Repository configuration loaded");
        }
        catch (Exception exception)
        {
            _ = Logger.LogE($"Repository configuration load failed ({exception.GetType().Name})");
            throw;
        }
    }

    private static void ClearCache()
    {
        _ = Logger.LogD("Clearing cache");

        foreach (string file in Directory.GetFiles(CachePath))
        {
            File.Delete(file);
        }

        foreach (string dir in Directory.GetDirectories(CachePath))
        {
            Directory.Delete(dir, true);
        }
        _ = Logger.LogD("Cache cleared");
    }

    public static void ExportRepoConfig(string destFile)
    {
        _ = Logger.LogI("Configuration export started");
        try
        {
            if (!File.Exists(ReposConfigFilePath))
            {
                _ = Logger.LogW("Configuration export skipped: configuration does not exist");
                return;
            }

            File.Copy(ReposConfigFilePath, destFile, true);
            _ = Logger.LogI("Configuration export completed");
        }
        catch (Exception exception)
        {
            _ = Logger.LogE($"Configuration export failed ({exception.GetType().Name})");
            throw;
        }
    }

    public static async Task ImportRepoConfig(string sourceFile)
    {
        _ = Logger.LogI("Configuration import started");
        try
        {
            if (!File.Exists(sourceFile))
            {
                _ = Logger.LogW("Configuration import skipped: source does not exist");
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
                repo.ArchiveInstallation = null;
            }
            _ = Logger.LogI("Configuration import completed");
        }
        catch (Exception exception)
        {
            _ = Logger.LogE($"Configuration import failed ({exception.GetType().Name})");
            throw;
        }
    }
}
