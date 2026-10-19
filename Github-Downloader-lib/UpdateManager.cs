using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using FileLib;
using Github_Downloader_lib.Models;
using Github_Downloader.Enums;
using LoggerLib;
using SecretsLib;

namespace Github_Downloader_lib;

public static class UpdateManager
{
    public static ObservableCollection<Repo> Repos;
    public static Platform CurPlatform;
    public static string? RootPassword;

    public static void WatchRepos()
    {
        if (Repos is null) return;
        
        Repos.CollectionChanged += Repos_CollectionChanged;
        foreach (Repo repo in Repos)
        {
            repo.PropertyChanged += Repo_PropertyChanged;
        }
    }

    private static void Repos_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (Repo repo in e.NewItems)
            {
                repo.PropertyChanged += Repo_PropertyChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (Repo repo in e.OldItems)
            {
                repo.PropertyChanged -= Repo_PropertyChanged;
            }
        }
        
        Logger.LogD($"Repository list changed: {e.Action}");
        FileManager.SaveRepos();
    }

    private static void Repo_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        string[] persistedProperties = 
        { 
            nameof(Repo.AssetName),
            nameof(Repo.AssetRule),
            nameof(Repo.ExcludedFromDownloadAll), 
            nameof(Repo.TargetTag),
            nameof(Repo.IncludePrereleases),
            nameof(Repo.Tag), 
            nameof(Repo.DownloadPath),
            nameof(Repo.SaveFileAnyway),
            nameof(Repo.AutoExtract),
            nameof(Repo.NewFileName),
            nameof(Repo.InstalledDebianPackage),
            nameof(Repo.InstalledAppImageId),
            nameof(Repo.CurrentInstallTag),
            nameof(Repo.LastDownloadTag)
        };

        if (e.PropertyName == nameof(Repo.IsSelected)) Logger.LogI("Repository selection changed");
        if (persistedProperties.Contains(e.PropertyName))
        {
            Logger.LogD($"Repository setting changed: {e.PropertyName}");
            FileManager.SaveRepos();
        }
    }
    
    internal readonly record struct Asset(Repo Repo, string TempAssetPath, string Tag, string? TemporaryDirectory = null,
        Guid? ParentHistoryId = null, Guid? BatchId = null, string? LocalSourcePath = null,
        string? ExtractionDirectory = null, Action<string, bool>? ExtractionFileWritten = null);

    public static async Task<Repo?> AddRepo(string repoUrl)
    {
        string publisherName = "";
        string repoName = "";
        
        try
        {
            string[] values = repoUrl.Split("github.com/");
            string[] values2 = values[1].TrimEnd('/').Split("/");
            publisherName = values2[0];
            repoName = values2[1];
        }
        catch (Exception) {
            Logger.LogW("Repository URL could not be parsed");
            return null;
        }

        return await AddRepo(publisherName, repoName);
    }
    
    public static async Task<Repo?> AddRepo(string publisherName, string repoName)
    {
        string url = $"https://api.github.com/repos/{publisherName}/{repoName}/releases/latest";
        string repoUrl = $"https://api.github.com/repos/{publisherName}/{repoName}";
        
        Logger.LogI("Adding repository");
        
        using HttpResponseMessage? httpRepoResponse = await Api.GetRequest(repoUrl, SecretsManager.LookupSecret("pat"));
        if (httpRepoResponse is not { IsSuccessStatusCode: true })
        {
            Logger.LogE("Failed to fetch repository");
            if (httpRepoResponse != null)
            {
                Logger.LogE(httpRepoResponse.StatusCode.ToString());
            }
            return null;
        }
        
        RepoResponse repoResponse = JsonSerializer.Deserialize<RepoResponse>(await httpRepoResponse.Content.ReadAsStringAsync());
        
        Repo repo = new()
        {
            Url = url,
            Name = repoResponse.full_name,
            Description = repoResponse.description,
            AssetRule = AssetSelection.GetDefaultRule()
        };

        Logger.LogI("Repository added");
        return repo;
    }

    public static async Task UpdateRepoDetails(IEnumerable<Repo> repos)
    {
        Logger.LogI("Updating repo-details");
        
        foreach (Repo repo in repos.ToArray())
        {
            using HttpResponseMessage? httpRepoResponse = await Api.GetRequest(repo.Url.Replace("/releases/latest", ""), SecretsManager.LookupSecret("pat"));
            if (httpRepoResponse is not { IsSuccessStatusCode: true })
            {
                Logger.LogE("Failed to fetch repository details");
                if (httpRepoResponse != null)
                {
                    Logger.LogE(httpRepoResponse.StatusCode.ToString());
                }
                continue;
            }
        
            RepoResponse? repoResponse = JsonSerializer.Deserialize<RepoResponse>(await httpRepoResponse.Content.ReadAsStringAsync());
            if (repoResponse == null)
            {
                Logger.LogE("Failed to parse repository details");
                continue;
            }
            
            repo.Name = repoResponse.full_name;
            repo.Description = repoResponse.description;
            repo.GitHubLink = repoResponse.html_url;

            if (repo.AssetNames.Count == 0)
            {
                await SearchForUpdates(repo, _ => { }, true);
            }
        }
    }

    private static Task<HttpResponseMessage?> RequestRepositoryData(string url) =>
        Api.GetRequest(url, SecretsManager.LookupSecret("pat"));

    public static Task<bool> SearchForUpdates(IEnumerable<Repo> repos, Action<string> statusText) =>
        SearchForUpdates(repos, statusText, RequestRepositoryData);

    internal static async Task<bool> SearchForUpdates(IEnumerable<Repo> repos, Action<string> statusText,
        Func<string, Task<HttpResponseMessage?>> request)
    {
        Repo[] snapshot = repos.ToArray();
        Logger.LogI($"Checking for updates (repositories {snapshot.Length})");
        int failed = 0;
        foreach (Repo repo in snapshot)
        {
            statusText($"Checking for {repo.Name}");
            if (!await SearchForUpdates(repo, statusText, request, true)) failed++;
        }
        string summary = $"Update check finished: {snapshot.Length - failed} succeeded, {failed} failed.";
        if (failed > 0)
        {
            await Logger.LogW(summary);
            statusText(summary + " Previous release information was kept for failed checks.");
        }
        else
        {
            await Logger.LogI(summary);
            statusText(summary);
        }
        return failed == 0;
    }

    private enum ReleaseLookup
    {
        Found,
        NotFound,
        Failed
    }

    public static Task<bool> SearchForUpdates(Repo repo, Action<string> statusText, bool multiDownload = false) =>
        SearchForUpdates(repo, statusText, RequestRepositoryData, multiDownload);

    internal static async Task<bool> SearchForUpdates(Repo repo, Action<string> statusText,
        Func<string, Task<HttpResponseMessage?>> request, bool multiDownload = false)
    {
        Logger.LogD("Checking repository for updates");
        
        if (!multiDownload)
        {
            statusText.Invoke($"Checking for {repo.Name}");
        }

        List<Response>? releases = await GetReleaseList(repo, request);
        if (releases == null)
        {
            statusText($"Failed to check releases for {repo.Name}. Previous release information was kept.");
            return false;
        }

        List<string> tags = ["latest"];
        tags.AddRange(releases.Where(release => !release.draft
            && (repo.IncludePrereleases || !release.prerelease)).Select(release => release.tag_name).Distinct());
        // A pin remains selectable even when it is outside the channel or no longer published.
        if (!tags.Contains(repo.TargetTag)) tags.Add(repo.TargetTag);
        repo.Tags = tags;

        (ReleaseLookup lookup, Response? response) = repo.TargetTag != "latest"
            ? await GetReleaseByUrl($"https://api.github.com/repos/{repo.Name}/releases/tags/{Uri.EscapeDataString(repo.TargetTag)}", request)
            : (ReleaseLookup.Found, releases.Where(release => !release.draft
                && (repo.IncludePrereleases || !release.prerelease))
                .OrderByDescending(release => DateTimeOffset.TryParse(release.published_at, out DateTimeOffset published)
                    ? published : DateTimeOffset.MinValue).FirstOrDefault());
        if (lookup == ReleaseLookup.Failed)
        {
            statusText.Invoke($"Failed to check releases for {repo.Name}. The request failed; previous release information was kept.");
            return false;
        }

        if (response == null || response.draft)
        {
            Logger.LogD("Repository has no available release");
            repo.HasRelease = false;
            repo.ReplaceAssets([], []);
            repo.Tag = "";
            repo.LatestChangelog = "";
            repo.ReleaseDate = "";
            statusText(repo.ReleaseStatus);
            return true;
        }

        Assets[] assets = response.assets ?? [];
        repo.ReplaceAssets(assets.Select(asset => asset.name), assets.Select(asset => asset.url));
        repo.LatestChangelog = response.body;
        repo.ReleaseDate = response.published_at;
        repo.HasRelease = true;
        // Set Tag last: it is a persisted property, so its change triggers SaveRepos()
        // and this writes the whole, fully-updated release state to disk.
        repo.Tag = response.tag_name;

        Logger.LogD("Repository release information refreshed");
        return true;
    }

    private static async Task<(ReleaseLookup Lookup, Response? Response)> GetReleaseByUrl(string url, Func<string, Task<HttpResponseMessage?>> request)
    {
        using HttpResponseMessage? httpResponse = await request(url);
        if (httpResponse == null)
        {
            return (ReleaseLookup.Failed, null);
        }

        if (httpResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return (ReleaseLookup.NotFound, null);
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            Logger.LogE($"Release check failed (HTTP {(int)httpResponse.StatusCode})");
            return (ReleaseLookup.Failed, null);
        }

        return (ReleaseLookup.Found,
            JsonSerializer.Deserialize<Response>(await httpResponse.Content.ReadAsStringAsync()));
    }

    private static async Task<List<Response>?> GetReleaseList(Repo repo, Func<string, Task<HttpResponseMessage?>> request)
    {
        List<Response> releases = [];
        for (int page = 1; ; page++)
        {
            string releasesUrl = $"https://api.github.com/repos/{repo.Name}/releases?per_page=100"
                + (page == 1 ? "" : $"&page={page}");
            using HttpResponseMessage? httpResponse = await request(releasesUrl);
            if (httpResponse is not { IsSuccessStatusCode: true })
            {
                Logger.LogW("Release list could not be refreshed");
                return null;
            }

            List<Response>? items = JsonSerializer.Deserialize<List<Response>>(await httpResponse.Content.ReadAsStringAsync());
            if (items == null) return null;
            releases.AddRange(items);
            // Search every page: GitHub's list order does not guarantee publication order.
            bool hasNext = httpResponse.Headers.TryGetValues("Link", out IEnumerable<string>? links)
                && links.Any(link => link.Contains("rel=\"next\"", StringComparison.Ordinal));
            if (!hasNext && items.Count < 100) return releases;
        }
    }

    public static bool IsInstallerAsset(string name)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();
        return OperatingSystem.IsLinux() && extension is ".deb" or ".appimage"
            || extension is ".exe" or ".msi";
    }

    public static async Task<bool> UpdateRepo(Repo repo, Action<string> statusText,
        Action<string> progressText, bool downloadAnyways = false)
        => await RunRepositoryAsync(repo, statusText, progressText, downloadAnyways) is not HistoryStatus.Failed;

    public static Task<bool> UpdateReposAsync(IEnumerable<Repo> repos, Action<string> statusText,
        Action<string> progressText, bool downloadAnyways = false,
        Action<Repo, HistoryStatus>? completed = null, bool respectExclusions = true) =>
        RunBatchAsync(repos, statusText, progressText, downloadAnyways, downloadOnly: false,
            completed: completed, respectExclusions: respectExclusions);

    public static Task<bool> DownloadReposAsync(IEnumerable<Repo> repos, Action<string> statusText,
        Action<string> progressText, bool downloadAnyways = false, Action<Repo, HistoryStatus>? completed = null) =>
        RunBatchAsync(repos, statusText, progressText, downloadAnyways, downloadOnly: true, completed: completed);

    internal static async Task<bool> RunBatchAsync(IEnumerable<Repo> repos, Action<string> statusText,
        Action<string> progressText, bool force, bool downloadOnly,
        Func<string, string, IProgress<double>, Task>? downloadFile = null,
        Func<Asset, Task<bool>>? installAsset = null, string? cachePath = null,
        Action<Repo, HistoryStatus>? completed = null, bool respectExclusions = true)
    {
        Repo[] snapshot = repos.ToArray();
        using HistoryScope batch = OperationHistory.Store.Begin(downloadOnly ? HistoryAction.Download
            : force ? HistoryAction.Reinstall : HistoryAction.Update, isBatch: true);
        int succeeded = 0;
        int failed = 0;
        int skipped = 0;
        statusText("Downloading updates...");
        foreach (Repo repo in snapshot)
        {
            HistoryStatus result = await RunRepositoryAsync(repo, statusText, progressText, force,
                downloadOnly, batch.Id, respectExclusion: !downloadOnly && respectExclusions,
                downloadFile: downloadFile, installAsset: installAsset, cachePath: cachePath);
            completed?.Invoke(repo, result);
            if (result == HistoryStatus.Skipped) skipped++;
            else if (result == HistoryStatus.Succeeded) succeeded++;
            else failed++;
        }
        batch.Finish(failed == 0 ? HistoryStatus.Succeeded : succeeded > 0 ? HistoryStatus.PartialFailure
            : HistoryStatus.Failed, HistoryOutcome.BatchCompleted, succeeded, failed, skipped);
        return failed == 0;
    }

    internal static async Task<HistoryStatus> RunRepositoryAsync(Repo repo, Action<string> statusText,
        Action<string> progressText, bool force = false, bool downloadOnly = false,
        Guid? batchId = null, bool respectExclusion = false,
        Func<string, string, IProgress<double>, Task>? downloadFile = null,
        Func<Asset, Task<bool>>? installAsset = null, string? cachePath = null)
    {
        using HistoryScope operation = OperationHistory.Store.Begin(downloadOnly ? HistoryAction.Download
            : force ? HistoryAction.Reinstall : HistoryAction.Update, repo, batchId, batchId: batchId);
        if (respectExclusion && repo.ExcludedFromDownloadAll || !force
            && (downloadOnly ? repo.LastDownloadTag == repo.Tag : repo.IsUpToDate))
        {
            operation.Finish(HistoryStatus.Skipped, respectExclusion && repo.ExcludedFromDownloadAll
                ? HistoryOutcome.Excluded : HistoryOutcome.AlreadyCurrent);
            return HistoryStatus.Skipped;
        }
        bool downloaded = false;
        try
        {
            Asset? asset = await DownloadAsset(repo, statusText, progressText,
                operation.Id, batchId, downloadFile, cachePath);
            if (asset is null)
            {
                operation.Finish(HistoryStatus.Failed, HistoryOutcome.SelectionUnresolved);
                return HistoryStatus.Failed;
            }
            downloaded = true;
            bool success = await ApplyAssetAsync(asset.Value, statusText, progressText, downloadOnly, installAsset);
            bool extract = !downloadOnly && repo.AutoExtract && ArchiveExtractor.IsSupportedArchive(asset.Value.TempAssetPath);
            bool install = !downloadOnly && IsInstallerAsset(asset.Value.TempAssetPath);
            operation.Finish(success ? HistoryStatus.Succeeded : HistoryStatus.Failed, success
                ? extract ? HistoryOutcome.Extracted : install ? HistoryOutcome.Installed : HistoryOutcome.Saved
                : extract ? HistoryOutcome.ExtractionFailed : install ? HistoryOutcome.InstallFailed : HistoryOutcome.SaveFailed);
            return success ? HistoryStatus.Succeeded : HistoryStatus.Failed;
        }
        catch (Exception exception)
        {
            Logger.LogE($"Repository operation failed ({exception.GetType().Name})");
            progressText("Repository operation failed. Check connectivity, permissions, and installation requirements.");
            operation.Finish(HistoryStatus.Failed, downloaded ? HistoryOutcome.Failed : HistoryOutcome.DownloadFailed);
            return HistoryStatus.Failed;
        }
    }

    internal static async Task<bool> UpdateReposAsync(List<Asset?> assets, Action<string> statusText,
        Action<string> progressText, Func<Asset, Task<bool>>? installAsset = null)
    {
        bool succeeded = true;
        foreach (Asset? asset in assets)
            if (asset.HasValue)
                succeeded &= await ApplyAssetAsync(asset.Value, statusText, progressText, false, installAsset);
        return succeeded;
    }

    internal static async Task<bool> ApplyAssetAsync(Asset asset, Action<string> statusText,
        Action<string> progressText, bool downloadOnly, Func<Asset, Task<bool>>? installAsset)
    {
        bool extract = !downloadOnly && asset.Repo.AutoExtract && ArchiveExtractor.IsSupportedArchive(asset.TempAssetPath);
        bool install = !downloadOnly && IsInstallerAsset(asset.TempAssetPath);
        string displayName = asset.LocalSourcePath is null ? asset.Repo.Name : Path.GetFileName(asset.LocalSourcePath);
        using HistoryScope history = OperationHistory.Store.Begin(extract ? HistoryAction.Extract
            : install ? HistoryAction.Install : HistoryAction.Save, asset.Repo, asset.ParentHistoryId,
            batchId: asset.BatchId, toVersion: asset.Tag, localFileName: asset.LocalSourcePath is null ? null : Path.GetFileName(asset.LocalSourcePath));
        try
        {
            if (asset.LocalSourcePath is string source)
            {
                statusText("Preparing local file...");
                Directory.CreateDirectory(asset.TemporaryDirectory!);
                await Task.Run(() => File.Copy(source, asset.TempAssetPath));
            }
            if (extract)
            {
                statusText($"Extracting {displayName}");
                await ArchiveExtractor.ExtractDownloadAsync(asset.Repo, asset.TempAssetPath, asset.Tag, progressText,
                    asset.ExtractionDirectory, asset.LocalSourcePath, asset.ExtractionFileWritten);
            }
            else
            {
                if (!install || asset.Repo.SaveFileAnyway)
                {
                    statusText($"Move file {displayName}");
                    CopyFile(asset);
                    asset.Repo.LastDownloadTag = asset.Tag;
                }
                if (install)
                {
                    statusText($"Installing {displayName}");
                    if (!await (installAsset ?? InstallAssetAsync)(asset))
                    {
                        history.Finish(HistoryStatus.Failed, HistoryOutcome.InstallFailed);
                        return false;
                    }
                    asset.Repo.CurrentInstallTag = asset.Tag;
                }
            }
            history.Complete(extract ? HistoryOutcome.Extracted : install ? HistoryOutcome.Installed : HistoryOutcome.Saved);
            return true;
        }
        catch (Exception exception)
        {
            string action = extract ? "Extraction" : install ? "Installation" : "Saving downloaded file";
            Logger.LogE($"{action} failed ({exception.GetType().Name})");
            progressText($"{action} failed ({exception.GetType().Name})");
            history.Finish(HistoryStatus.Failed, extract ? HistoryOutcome.ExtractionFailed
                : install ? HistoryOutcome.InstallFailed : HistoryOutcome.SaveFailed);
            return false;
        }
        finally
        {
            if (asset.TemporaryDirectory is string directory && Directory.Exists(directory))
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (IOException) { Logger.LogW("Temporary download cleanup failed"); }
                catch (UnauthorizedAccessException) { Logger.LogW("Temporary download cleanup failed"); }
            }
        }
    }

    private static async Task<bool> InstallAssetAsync(Asset asset)
    {
        if (OperatingSystem.IsLinux() && asset.TempAssetPath.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
        {
            InstallationManager installations = new();
            string package = await installations.ReadDebianPackageNameAsync(asset.TempAssetPath);
            if (!await InstallDebsAsync([asset.TempAssetPath], _ => { })) return false;
            asset.Repo.InstalledDebianPackage = package;
            return true;
        }
        if (OperatingSystem.IsLinux() && asset.TempAssetPath.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase))
        {
            await HandleAppImagesAsync([asset]);
            asset.Repo.InstalledAppImageId = InstallationManager.GetAppImageId(asset.Repo);
            return true;
        }
        return await InstallExeAsync([asset.TempAssetPath], _ => { });
    }

    private static async Task HandleAppImagesAsync(List<Asset> assets)
    {
        foreach (Asset asset in assets)
        {
            Logger.LogI("Installing AppImage");
            
            string appImageId = InstallationManager.GetAppImageId(asset.Repo);
            string assetPath = Path.Join(FileManager.AppImagesPath, appImageId);
            DirectoryHelper.CreateDir(assetPath);
            string destPath = Path.Join(assetPath, appImageId + ".AppImage");
            string iconPath = Path.Join(assetPath, "icon.png");
            File.Move(asset.TempAssetPath, destPath, overwrite: true);

            await RunAppImageCommandAsync(new ProcessStartInfo
            {
                FileName = "chmod", ArgumentList = { "+x", destPath },
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            }, "AppImage permission update");
            await RunAppImageCommandAsync(new ProcessStartInfo
            {
                FileName = destPath, ArgumentList = { "--appimage-extract" },
                WorkingDirectory = FileManager.CachePath,
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            }, "AppImage resource extraction");

            string tempIconPath = Path.Combine(FileManager.CachePath, "squashfs-root", ".DirIcon");
            do
            {
                FileInfo fileInfo = new(tempIconPath);
                if (fileInfo.LinkTarget != null)
                {
                    tempIconPath = Path.Join(FileManager.CachePath, "squashfs-root", fileInfo.LinkTarget);
                }
            } while (new FileInfo(tempIconPath).LinkTarget != null);

            File.Move(tempIconPath, iconPath, overwrite: true);

            CreateStartMenuEntry(asset with { TempAssetPath = destPath }, iconPath);
            Logger.LogI("AppImage installed");
        }
    }

    private static async Task RunAppImageCommandAsync(ProcessStartInfo start, string action)
    {
        Logger.LogI($"{action} started");
        using Process process = new() { StartInfo = start };
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        // Drain both streams without logging paths or file names printed by AppImage tools.
        await Task.WhenAll(output, errors);
        if (process.ExitCode != 0)
        {
            Logger.LogE($"{action} failed (exit code {process.ExitCode})");
            throw new InvalidOperationException($"{action} failed (exit code {process.ExitCode}).");
        }
        Logger.LogI($"{action} completed");
    }

    private static void CreateStartMenuEntry(Asset asset, string iconPath)
    {
        string displayName = asset.LocalSourcePath is null ? asset.Repo.Name
            : Path.GetFileNameWithoutExtension(asset.LocalSourcePath);
        displayName = displayName.Replace('\r', ' ').Replace('\n', ' ').Replace("\\", "\\\\");
        string desktopFile = $"""
                             [Desktop Entry]
                             Name={displayName}
                             Comment={asset.Repo.Description}
                             GenericName={displayName}
                             Exec={asset.TempAssetPath}
                             Icon={iconPath}
                             Type=Application
                             StartupNotify=false
                             Categories=Utility;
                             """;

        string desktopDirectoryPath = Path.Join(DirectoryHelper.GetUserDirPath(), ".local", "share", "applications");
        string desktopFilePath = Path.Join(desktopDirectoryPath, InstallationManager.GetAppImageId(asset.Repo) + ".desktop");
        DirectoryHelper.CreateDir(desktopDirectoryPath);
        FileHelper.Create(desktopFilePath);
        File.WriteAllText(desktopFilePath, desktopFile);
    }
        
    private static async Task<Asset?> DownloadAsset(Repo repo, Action<string> statusText, Action<string> progressText,
        Guid? parentId = null, Guid? batchId = null,
        Func<string, string, IProgress<double>, Task>? downloadFile = null, string? cachePath = null)
    {
        Logger.LogI("Preparing asset download");
        
        using HistoryScope history = OperationHistory.Store.Begin(HistoryAction.Download, repo, parentId, batchId: batchId);
        statusText.Invoke($"Downloading {repo.Name}");
        
        Progress<double> progress = new(p =>
        {
            progressText.Invoke($"Downloaded: {p:0.00}%");
        });
        
        string downloadAssetName = repo.SelectedAssetName;
        string downloadAssetUrl = repo.SelectedAssetUrl;
        if (downloadAssetName == "" || downloadAssetUrl == "")
        {
            Logger.LogW("No release asset selected for download");
            string message = $"Skipped {repo.Name}: {repo.AssetSelectionStatus}";
            statusText.Invoke(message);
            progressText.Invoke(message);
            history.Finish(HistoryStatus.Failed, HistoryOutcome.SelectionUnresolved);
            return null;
        }

        string downloadTag = repo.Tag;
        cachePath ??= FileManager.CachePath;
        string? temporaryDirectory = repo.AutoExtract && ArchiveExtractor.IsSupportedArchive(downloadAssetName)
            ? Path.Combine(cachePath, $"archive-{Guid.NewGuid():N}")
            : null;
        string temporaryPath = Path.Combine(temporaryDirectory ?? cachePath, downloadAssetName);
        if (temporaryDirectory != null) Directory.CreateDirectory(temporaryDirectory);
        try
        {
            if (downloadFile is null)
                await Api.DownloadFileAsync(downloadAssetUrl, temporaryPath, SecretsManager.LookupSecret("pat"), progress);
            else await downloadFile(downloadAssetUrl, temporaryPath, progress);
            history.Complete(HistoryOutcome.Downloaded);
        }
        catch
        {
            history.Finish(HistoryStatus.Failed, HistoryOutcome.DownloadFailed);
            if (temporaryDirectory != null) Directory.Delete(temporaryDirectory, recursive: true);
            throw;
        }

        Asset asset = new()
        {
            Repo = repo,
            TempAssetPath = temporaryPath,
            Tag = downloadTag,
            TemporaryDirectory = temporaryDirectory,
            ParentHistoryId = parentId,
            BatchId = batchId
        };
        
        return asset;
    }

    private static void CopyFile(Asset asset)
    {
        string destName = asset.Repo.NewFileName == "" ? asset.Repo.SelectedAssetName : asset.Repo.NewFileName;
        string destPath = Path.Join(asset.Repo.DownloadPath, destName);
        if (File.Exists(destPath))
        {
            File.Delete(destPath);
        }
        
        Logger.LogI("Copying downloaded asset");
        File.Copy(Path.Join(asset.TempAssetPath), destPath);
        Logger.LogI("Downloaded asset copied");
    }

    private static async Task<bool> InstallDebsAsync(List<string> debPaths, Action<string> progressText)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return true;
        }

        string[] packages = debPaths.Where(path => Path.GetExtension(path).Equals(".deb", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath).ToArray();
        if (packages.Length == 0) return true;

        bool usePkexec = CurPlatform == Platform.Avalonia;
        bool providePassword = !usePkexec && !string.IsNullOrEmpty(RootPassword);

        Logger.LogI($"Installing Debian packages (count {debPaths.Count})");
        progressText("Installing Debian packages...");
        
        using Process process = new()
        {
            StartInfo = new()
            {
                FileName = "/usr/bin/" + (usePkexec ? "pkexec" : "sudo"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = providePassword,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        
        if (providePassword)
        {
            process.StartInfo.ArgumentList.Add("-S");
            process.StartInfo.ArgumentList.Add("-p");
            process.StartInfo.ArgumentList.Add("");
        }
        process.StartInfo.ArgumentList.Add("/usr/bin/apt-get");
        foreach (string argument in new[] { "install", "-y", "--allow-downgrades", "--reinstall", "--" })
            process.StartInfo.ArgumentList.Add(argument);
        foreach (string package in packages) process.StartInfo.ArgumentList.Add(package);
        process.Start();

        if (providePassword)
        {
            await process.StandardInput.WriteLineAsync(RootPassword);
            process.StandardInput.Close();
        }

        // Package managers print private paths. Keep diagnostic logs to action names and exit codes.
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        await Task.WhenAll(output, errors);
        
        if (process.ExitCode == 0)
        {
            Logger.LogI("Installation complete");
            return true;
        }

        string hint = providePassword && process.ExitCode == 1
            ? " (the supplied root password may be incorrect)"
            : "";
        Logger.LogE($"Installation failed with exit code {process.ExitCode}");
        progressText($"Installation failed with exit code {process.ExitCode}{hint}");
        return false;
    }

    private static async Task<bool> InstallExeAsync(List<string> exePaths, Action<string> progressText)
    {
        bool allOk = true;
        foreach (string exePath in exePaths)
        {
            Logger.LogI("Starting executable installer");
            
            using Process process = new()
            {
                StartInfo = new()
                    {
                    FileName = exePath,
                    UseShellExecute = true // Important to open GUI installer
                }
            };

            process.Start();

            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0)
            {
                Logger.LogI("Installation complete");
            }
            else
            {
                Logger.LogE($"Installation failed with exit code {process.ExitCode}");
                allOk = false;
            }
        }

        return allOk;
    }
}
