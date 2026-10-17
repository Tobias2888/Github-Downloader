using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using SecretsLib;
using LoggerLib;
using DTOs = Github_Downloader_Web.DTOs;

namespace Github_Downloader_Web.Services;

public interface IGithubDownloaderService
{
    Task<DTOs.RepoResponse> AddRepoAsync(string repoUrl);
    Task<DTOs.RepoResponse> AddRepoAsync(string publisherName, string repoName);
    Task<DTOs.ReposListResponse> GetAllReposAsync();
    Task<DTOs.RepoResponse?> GetRepoAsync(string repoUrl);
    Task<DTOs.RepoResponse?> UpdateRepoAsync(string repoUrl, DTOs.UpdateRepoRequest request);
    Task<bool> DeleteRepoAsync(string repoUrl);
    Task<DTOs.ConfigOperationResponse> ExportConfigAsync(string destinationPath);
    Task<DTOs.ConfigOperationResponse> ImportConfigAsync(string sourcePath);
    Task<DTOs.SearchUpdatesResponse> SearchUpdatesAsync(List<string>? repoUrls = null);
    Task<DTOs.DownloadResponse> DownloadAssetsAsync(List<string> repoUrls, bool downloadAnyways = false, IProgress<DTOs.DownloadProgress>? progress = null);
    Task<DTOs.InstallResponse> InstallAssetsAsync(List<string> repoUrls, bool downloadAnyways = false, IProgress<DTOs.DownloadProgress>? progress = null, string? password = null);
    Task<DTOs.RootStatusResponse> GetRootStatusAsync();
    Task<DTOs.HealthResponse> GetHealthAsync();
    Task<DTOs.SettingsInfoResponse> GetSettingsInfoAsync();
    Task<DTOs.PatStatusResponse> GetPatStatusAsync();
    Task StorePatAsync(string pat);
    Task ClearPatAsync();
    Task<DTOs.ClearDataResponse> ClearAllDataAsync();
    Task InitializeAsync();
}

public class GithubDownloaderService : IGithubDownloaderService
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly OperationTracker _tracker;
    private bool _initialized = false;

    [DllImport("libc", SetLastError = true)]
    private static extern uint geteuid();

    public GithubDownloaderService(OperationTracker tracker)
    {
        _tracker = tracker;
    }

    private (Action<string> Status, Action<string> Progress) CreateCallbacks()
    {
        Action<string> status = new Action<string>(message =>
        {
            if (string.IsNullOrEmpty(message)) return;

            if (message.StartsWith("Downloading ") && !message.StartsWith("Downloading updates"))
            {
                _tracker.StartItem();
            }
            else if (message.StartsWith("Checking for "))
            {
                _tracker.StartItem();
            }

            _tracker.Log(message);
        });

        Action<string> progress = new Action<string>(message =>
        {
            if (string.IsNullOrEmpty(message)) return;

            const string prefix = "Downloaded: ";
            if (message.StartsWith(prefix) && message.EndsWith('%'))
            {
                // The library formats this with the current culture (e.g. "45,50" on de-AT), so
                // parse with the current culture first and fall back to invariant for "45.50".
                // NumberStyles.Float is used deliberately: Any would treat the decimal comma as a
                // group separator and parse "45,50" as 4550.
                string number = message[prefix.Length..^1].Trim();
                if (double.TryParse(number, NumberStyles.Float, CultureInfo.CurrentCulture, out double percent)
                    || double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
                {
                    _tracker.SetItemPercent(percent);
                    return;
                }
            }

            _tracker.Log(message, "detail");
        });

        return (status, progress);
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        
        await _initializationLock.WaitAsync();
        try
        {
            if (_initialized) return;
            
            await FileManager.LoadRepos();
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<DTOs.RepoResponse> AddRepoAsync(string repoUrl)
    {
        await InitializeAsync();
        Repo? repo = await UpdateManager.AddRepo(repoUrl);
        if (repo == null)
            throw new InvalidOperationException($"Failed to add repository: {repoUrl}");
        
        await UpdateManager.SearchForUpdates(repo, _ => { });
        await UpdateManager.UpdateRepoDetails([repo]);
        UpdateManager.Repos?.Add(repo);
        FileManager.SaveRepos();
        
        return MapToRepoResponse(repo);
    }

    public async Task<DTOs.RepoResponse> AddRepoAsync(string publisherName, string repoName)
    {
        await InitializeAsync();
        Repo? repo = await UpdateManager.AddRepo(publisherName, repoName);
        if (repo == null)
            throw new InvalidOperationException($"Failed to add repository: {publisherName}/{repoName}");
        
        await UpdateManager.SearchForUpdates(repo, _ => { });
        await UpdateManager.UpdateRepoDetails([repo]);
        UpdateManager.Repos?.Add(repo);
        FileManager.SaveRepos();
        
        return MapToRepoResponse(repo);
    }

    public async Task<DTOs.ReposListResponse> GetAllReposAsync()
    {
        await InitializeAsync();
        List<DTOs.RepoResponse> repos = UpdateManager.Repos?.Select(MapToRepoResponse).ToList() ?? [];
        return new DTOs.ReposListResponse(repos, repos.Count);
    }

    public async Task<DTOs.RepoResponse?> GetRepoAsync(string repoUrl)
    {
        await InitializeAsync();
        Repo? repo = UpdateManager.Repos?.FirstOrDefault(r => r.Url == repoUrl);
        return repo != null ? MapToRepoResponse(repo) : null;
    }

    public async Task<DTOs.RepoResponse?> UpdateRepoAsync(string repoUrl, DTOs.UpdateRepoRequest request)
    {
        await InitializeAsync();
        Repo? repo = UpdateManager.Repos?.FirstOrDefault(r => r.Url == repoUrl);
        if (repo == null) return null;

        if (request.DownloadAssetIndex.HasValue
            && (request.DownloadAssetIndex.Value < -1 || request.DownloadAssetIndex.Value >= repo.AssetNames.Count))
            throw new ArgumentOutOfRangeException(nameof(request.DownloadAssetIndex), "Asset index is out of range.");

        Logger.LogI("Repository settings update requested");
        if (request.DownloadPath != null) repo.DownloadPath = request.DownloadPath;
        if (request.SaveFileAnyway.HasValue) repo.SaveFileAnyway = request.SaveFileAnyway.Value;
        if (request.AutoExtract.HasValue) repo.AutoExtract = request.AutoExtract.Value;
        if (request.NewFileName != null) repo.NewFileName = request.NewFileName;
        if (request.ExcludedFromDownloadAll.HasValue) repo.ExcludedFromDownloadAll = request.ExcludedFromDownloadAll.Value;
        if (request.TargetTag != null) repo.TargetTag = request.TargetTag;
        if (request.DownloadAssetIndex is >= 0 && string.IsNullOrEmpty(request.AssetRule))
        {
            repo.AssetRule = "";
            repo.DownloadAssetIndex = request.DownloadAssetIndex.Value;
        }
        if (request.AssetRule != null) repo.AssetRule = request.AssetRule;

        FileManager.SaveRepos();
        return MapToRepoResponse(repo);
    }

    public async Task<bool> DeleteRepoAsync(string repoUrl)
    {
        await InitializeAsync();
        Repo? repo = UpdateManager.Repos?.FirstOrDefault(r => r.Url == repoUrl);
        if (repo == null) return false;

        Logger.LogI("Removing repository from tracking");
        UpdateManager.Repos?.Remove(repo);
        FileManager.SaveRepos();
        return true;
    }

    public async Task<DTOs.ConfigOperationResponse> ExportConfigAsync(string destinationPath)
    {
        await InitializeAsync();
        try
        {
            FileManager.ExportRepoConfig(destinationPath);
            return new DTOs.ConfigOperationResponse(true, "Configuration exported successfully");
        }
        catch (Exception ex)
        {
            return new DTOs.ConfigOperationResponse(false, $"Export failed ({ex.GetType().Name})");
        }
    }

    public async Task<DTOs.ConfigOperationResponse> ImportConfigAsync(string sourcePath)
    {
        await InitializeAsync();
        try
        {
            await FileManager.ImportRepoConfig(sourcePath);
            return new DTOs.ConfigOperationResponse(true, "Configuration imported successfully");
        }
        catch (Exception ex)
        {
            return new DTOs.ConfigOperationResponse(false, $"Import failed ({ex.GetType().Name})");
        }
    }

    public async Task<DTOs.SearchUpdatesResponse> SearchUpdatesAsync(List<string>? repoUrls = null)
    {
        await InitializeAsync();
        List<Repo> reposToCheck = repoUrls != null && repoUrls.Count > 0
            ? UpdateManager.Repos?.Where(r => repoUrls.Contains(r.Url)).ToList() ?? []
            : UpdateManager.Repos?.ToList() ?? [];

        List<DTOs.RepoUpdateInfo> updates = new();

        _tracker.Start($"Checking {reposToCheck.Count} repositories", reposToCheck.Count);
        try
        {
            (Action<string> status, Action<string> _) = CreateCallbacks();
            foreach (Repo repo in reposToCheck)
            {
                await UpdateManager.SearchForUpdates(repo, status);

                bool hasUpdate = repo.Tag != repo.CurrentInstallTag && repo.Tag != "";
                updates.Add(new DTOs.RepoUpdateInfo(
                    repo.Url,
                    repo.Name,
                    repo.CurrentInstallTag,
                    repo.Tag,
                    hasUpdate,
                    repo.AssetNames.ToList(),
                    repo.LatestChangelog
                ));
            }

            _tracker.Finish(true);
            return new DTOs.SearchUpdatesResponse(updates, reposToCheck.Count);
        }
        catch (Exception ex)
        {
            Logger.LogE($"Web operation failed ({ex.GetType().Name})");
            _tracker.Finish(false, $"Operation failed ({ex.GetType().Name})");
            throw;
        }
    }

    public async Task<DTOs.DownloadResponse> DownloadAssetsAsync(List<string> repoUrls, bool downloadAnyways = false, IProgress<DTOs.DownloadProgress>? progress = null)
    {
        await InitializeAsync();
        List<Repo> repos = UpdateManager.Repos?.Where(r => repoUrls.Contains(r.Url)).ToList() ?? [];
        List<DTOs.DownloadedAsset> downloadedAssets = new();

        _tracker.Start($"Downloading {repos.Count} repositories", repos.Count);
        try
        {
            (Action<string> statusAction, Action<string> progressAction) = CreateCallbacks();
            int failed = 0;

            foreach (Repo repo in repos)
            {
                string initialTag = repo.CurrentInstallTag;
                bool succeeded = await UpdateManager.UpdateRepo(repo, statusAction, progressAction, downloadAnyways);
                if (!succeeded)
                {
                    failed++;
                    continue;
                }

                if (repo.CurrentInstallTag != initialTag || downloadAnyways)
                {
                    downloadedAssets.Add(new DTOs.DownloadedAsset(
                        repo.Url,
                        repo.Name,
                        repo.SelectedAssetName,
                        repo.Tag,
                        repo.AutoExtract && ArchiveExtractor.IsSupportedArchive(repo.SelectedAssetName)
                            ? repo.ExtractionDirectory
                            : Path.Join(FileManager.CachePath, repo.SelectedAssetName),
                        repo.AutoExtract && ArchiveExtractor.IsSupportedArchive(repo.SelectedAssetName)
                    ));

                    progress?.Report(new DTOs.DownloadProgress(
                        repo.Url,
                        repo.Name,
                        100,
                        $"Downloaded: {repo.Name}"
                    ));
                }
                else
                {
                    progress?.Report(new DTOs.DownloadProgress(
                        repo.Url,
                        repo.Name,
                        100,
                        $"Up to date: {repo.Name}"
                    ));
                }
            }

            string summary = $"Downloaded {downloadedAssets.Count} assets; {failed} failed or were skipped. Check the activity log for details.";
            _tracker.Finish(failed == 0, failed == 0 ? null : summary);
            return new DTOs.DownloadResponse(failed == 0, summary, downloadedAssets);
        }
        catch (Exception ex)
        {
            Logger.LogE($"Web operation failed ({ex.GetType().Name})");
            _tracker.Finish(false, $"Operation failed ({ex.GetType().Name})");
            throw;
        }
    }

    public async Task<DTOs.InstallResponse> InstallAssetsAsync(List<string> repoUrls, bool downloadAnyways = false, IProgress<DTOs.DownloadProgress>? progress = null, string? password = null)
    {
        await InitializeAsync();
        List<Repo> repos = UpdateManager.Repos?.Where(r => repoUrls.Contains(r.Url)).ToList() ?? [];
        List<DTOs.InstalledAsset> installedAssets = new();

        List<Repo> repoList = repos.Where(r => !r.ExcludedFromDownloadAll).ToList();
        // Only repos with an actual update are processed (repos already at the target tag are
        // skipped by the update loop), so count those for an accurate tracker.
        List<Repo> operationItems = downloadAnyways
            ? repos
            : repoList.Where(r => r.Tag != "" && r.Tag != r.CurrentInstallTag).ToList();

        _tracker.Start($"{(downloadAnyways ? "Reinstalling" : "Updating")} {operationItems.Count} repositories", operationItems.Count);
        string? previousPassword = UpdateManager.RootPassword;
        UpdateManager.RootPassword = string.IsNullOrEmpty(password) ? previousPassword : password;
        try
        {
            (Action<string> statusAction, Action<string> progressAction) = CreateCallbacks();

            bool installOk = true;
            List<Repo> changedRepos;
            if (downloadAnyways)
            {
                foreach (Repo repo in repos)
                {
                    installOk &= await UpdateManager.UpdateRepo(repo, statusAction, progressAction, true);
                }
                changedRepos = repos;
            }
            else
            {
                List<(Repo Repo, string Tag)> tagsBefore = repoList.Select(r => (Repo: r, Tag: r.CurrentInstallTag)).ToList();
                if (repoList.Count > 0)
                {
                    installOk &= await UpdateManager.UpdateReposAsync(repoList, statusAction, progressAction);
                }
                changedRepos = tagsBefore
                    .Where(t => t.Repo.CurrentInstallTag != t.Tag)
                    .Select(t => t.Repo)
                    .ToList();
            }

            if (!installOk)
            {
                throw new InvalidOperationException(
                    "The update failed. Check the activity log for extraction or installation errors; if an administrator password was requested, verify it is correct.");
            }

            foreach (Repo repo in changedRepos)
            {
                string assetName = repo.NewFileName == "" ?
                    repo.SelectedAssetName
                    : repo.NewFileName;

                installedAssets.Add(new DTOs.InstalledAsset(
                    repo.Url,
                    repo.Name,
                    assetName,
                    repo.Tag,
                    repo.AutoExtract && ArchiveExtractor.IsSupportedArchive(repo.SelectedAssetName)
                        ? repo.ExtractionDirectory
                        : Path.Join(repo.DownloadPath, assetName)
                ));
            }

            _tracker.Finish(true);
            string message = installedAssets.Count > 0
                ? $"Updated {installedAssets.Count} assets"
                : "Everything is up to date";
            return new DTOs.InstallResponse(true, message, installedAssets);
        }
        catch (Exception ex)
        {
            Logger.LogE($"Web operation failed ({ex.GetType().Name})");
            _tracker.Finish(false, $"Operation failed ({ex.GetType().Name})");
            throw;
        }
        finally
        {
            UpdateManager.RootPassword = previousPassword;
        }
    }

    public Task<DTOs.RootStatusResponse> GetRootStatusAsync()
    {
        bool isLinux = OperatingSystem.IsLinux();
        bool isRoot = isLinux && geteuid() == 0;
        bool requiresPassword = false;

        if (isLinux && !isRoot)
        {
            try
            {
                using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/sudo",
                    Arguments = "-n true",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null || !process.WaitForExit(5000))
                {
                    try { process?.Kill(); } catch { /* ignore */ }
                    requiresPassword = true;
                }
                else
                {
                    requiresPassword = process.ExitCode != 0;
                }
            }
            catch
            {
                requiresPassword = true;
            }
        }

        return Task.FromResult(new DTOs.RootStatusResponse(isLinux, isRoot, requiresPassword));
    }

    public async Task<DTOs.HealthResponse> GetHealthAsync()
    {
        await InitializeAsync();
        return new DTOs.HealthResponse(
            "Healthy",
            Github_Downloader_lib.AppInfo.Version,
            UpdateManager.Repos?.Count ?? 0,
            DateTime.UtcNow
        );
    }

    public async Task<DTOs.SettingsInfoResponse> GetSettingsInfoAsync()
    {
        await InitializeAsync();
        return new DTOs.SettingsInfoResponse(
            Github_Downloader_lib.AppInfo.Version,
            UpdateManager.Repos?.Count ?? 0
        );
    }

    public Task<DTOs.PatStatusResponse> GetPatStatusAsync()
    {
        bool hasPat = false;
        try
        {
            string? pat = SecretsManager.LookupSecret("pat");
            hasPat = !string.IsNullOrEmpty(pat);
        }
        catch
        {
            hasPat = false;
        }
        return Task.FromResult(new DTOs.PatStatusResponse(hasPat));
    }

    public Task StorePatAsync(string pat)
    {
        Logger.LogI("Saving personal access token");
        SecretsManager.StoreSecret("pat", pat);
        Logger.LogI("Personal access token saved");
        return Task.CompletedTask;
    }

    public Task ClearPatAsync()
    {
        Logger.LogI("Removing personal access token");
        SecretsManager.ClearSecret("pat");
        Logger.LogI("Personal access token removed");
        return Task.CompletedTask;
    }

    public async Task<DTOs.ClearDataResponse> ClearAllDataAsync()
    {
        await InitializeAsync();
        Logger.LogI("Clearing application data");
        try
        {
            if (UpdateManager.Repos != null)
            {
                UpdateManager.Repos.Clear();
            }
            FileManager.SaveRepos();

            if (Directory.Exists(FileManager.CachePath))
            {
                foreach (string file in Directory.GetFiles(FileManager.CachePath))
                {
                    File.Delete(file);
                }

                foreach (string dir in Directory.GetDirectories(FileManager.CachePath))
                {
                    Directory.Delete(dir, true);
                }
            }

            Logger.LogI("Application data cleared");
            return new DTOs.ClearDataResponse(true, "All data cleared successfully");
        }
        catch (Exception ex)
        {
            Logger.LogE($"Application data clear failed ({ex.GetType().Name})");
            return new DTOs.ClearDataResponse(false, $"Failed to clear data ({ex.GetType().Name})");
        }
    }

    private static DTOs.RepoResponse MapToRepoResponse(Repo repo)
    {
        return new DTOs.RepoResponse(
            repo.Url,
            repo.Name,
            repo.Description,
            repo.DownloadAssetIndex,
            repo.AssetNames.ToList(),
            repo.DownloadUrls,
            repo.Tag,
            repo.CurrentInstallTag,
            repo.TargetTag,
            repo.Tags,
            repo.ReleaseDate,
            repo.GitHubLink,
            repo.LatestChangelog,
            repo.IsUpToDate,
            repo.DownloadPath,
            repo.SaveFileAnyway,
            repo.NewFileName,
            repo.ExcludedFromDownloadAll,
            repo.IsSelected,
            repo.AutoExtract,
            repo.ExtractionDirectory,
            repo.AssetName,
            repo.AssetRule,
            repo.AssetRuleMatches,
            repo.AssetSelectionStatus
        );
    }
}
