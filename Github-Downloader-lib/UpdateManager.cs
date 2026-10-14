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
            nameof(Repo.DownloadAssetIndex), 
            nameof(Repo.ExcludedFromDownloadAll), 
            nameof(Repo.TargetTag), 
            nameof(Repo.Tag), 
            nameof(Repo.DownloadPath),
            nameof(Repo.SaveFileAnyway),
            nameof(Repo.AutoExtract),
            nameof(Repo.NewFileName),
            nameof(Repo.InstalledDebianPackage),
            nameof(Repo.InstalledAppImageId),
            nameof(Repo.CurrentInstallTag)
        };

        if (e.PropertyName == nameof(Repo.IsSelected)) Logger.LogI("Repository selection changed");
        if (persistedProperties.Contains(e.PropertyName))
        {
            Logger.LogD($"Repository setting changed: {e.PropertyName}");
            FileManager.SaveRepos();
        }
    }
    
    internal readonly record struct Asset(Repo Repo, string TempAssetPath, string Tag, string? TemporaryDirectory = null);

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
            Description = repoResponse.description
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

        bool tagsAvailable = await UpdateTags(repo, request);

        if (tagsAvailable && repo.TargetTag != "latest" && !repo.Tags.Contains(repo.TargetTag))
        {
            repo.TargetTag = "latest";
        }

        (ReleaseLookup lookup, Response? response) = await GetRelease(repo, repo.TargetTag, request);
        if (lookup == ReleaseLookup.Failed)
        {
            statusText.Invoke($"Failed to check releases for {repo.Name}. The request failed; previous release information was kept.");
            return false;
        }

        if (response == null)
        {
            Logger.LogD("Repository has no available release");
            repo.HasRelease = false;
            repo.AssetNames.Clear();
            repo.DownloadUrls = [];
            repo.Tag = "";
            repo.LatestChangelog = "";
            repo.ReleaseDate = "";
            return true;
        }

        int oldIndex = repo.DownloadAssetIndex;
        Assets[] assets = response.assets ?? [];
        repo.AssetNames.Clear();
        foreach (Assets asset in assets)
        {
            repo.AssetNames.Add(asset.name);
        }

        repo.DownloadUrls = assets.Select(asset => asset.url).ToList();
        repo.LatestChangelog = response.body;
        repo.ReleaseDate = response.published_at;
        repo.HasRelease = true;
        // Set Tag last: it is a persisted property, so its change triggers SaveRepos()
        // and this writes the whole, fully-updated release state to disk.
        repo.Tag = response.tag_name;

        repo.DownloadAssetIndex = Math.Clamp(oldIndex, 0, Math.Max(repo.AssetNames.Count - 1, 0));
        Logger.LogD("Repository release information refreshed");
        return true;
    }

    private static async Task<bool> UpdateTags(Repo repo, Func<string, Task<HttpResponseMessage?>> request)
    {
        string tagsUrl = $"https://api.github.com/repos/{repo.Name}/tags?per_page=100";
        using HttpResponseMessage? httpResponseTags = await request(tagsUrl);
        if (httpResponseTags is not { IsSuccessStatusCode: true })
        {
            Logger.LogW(httpResponseTags is null ? "Repository tags could not be refreshed"
                : $"Repository tags could not be refreshed (HTTP {(int)httpResponseTags.StatusCode})");
            return false;
        }

        List<TagsResponse>? tagsResponse = JsonSerializer.Deserialize<List<TagsResponse>>(await httpResponseTags.Content.ReadAsStringAsync());
        if (tagsResponse == null) return false;

        List<string> tags = ["latest"];
        tags.AddRange(tagsResponse.Select(tag => tag.name));
        repo.Tags = tags;
        return true;
    }

    private static async Task<(ReleaseLookup Lookup, Response? Response)> GetRelease(Repo repo, string targetTag, Func<string, Task<HttpResponseMessage?>> request)
    {
        if (targetTag != "latest")
        {
            return await GetReleaseByUrl($"https://api.github.com/repos/{repo.Name}/releases/tags/{targetTag}", request);
        }

        (ReleaseLookup lookup, Response? response) = await GetReleaseByUrl(repo.Url, request);
        if (lookup == ReleaseLookup.Found)
        {
            return (lookup, response);
        }
        if (lookup == ReleaseLookup.Failed)
        {
            return (lookup, null);
        }

        List<Response>? releases = await GetReleaseList(repo, request);
        if (releases == null)
        {
            return (ReleaseLookup.Failed, null);
        }

        Response? fallback = releases.FirstOrDefault(release => !release.draft && !release.prerelease)
            ?? releases.FirstOrDefault(release => !release.draft);
        return fallback == null
            ? (ReleaseLookup.NotFound, null)
            : (ReleaseLookup.Found, fallback);
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
        string releasesUrl = $"https://api.github.com/repos/{repo.Name}/releases?per_page=100";
        using HttpResponseMessage? httpResponse = await request(releasesUrl);
        if (httpResponse is not { IsSuccessStatusCode: true })
        {
            if (httpResponse is not null)
                Logger.LogE($"Release list check failed (HTTP {(int)httpResponse.StatusCode})");
            return null;
        }

        return JsonSerializer.Deserialize<List<Response>>(await httpResponse.Content.ReadAsStringAsync());
    }

    public static async Task<bool> UpdateRepo(Repo repo, Action<string> statusText, Action<string> progressText, bool downloadAnyways = false)
    {
        if (!downloadAnyways && repo.Tag == repo.CurrentInstallTag)
        {
            Logger.LogD("Repository update skipped: already up to date");
            return true;
        }
        Asset? asset = await DownloadAsset(repo, statusText, progressText, downloadAnyways);
        if (asset == null) return false;
        return await UpdateReposAsync([asset], statusText, progressText);
    }

    public static async Task<bool> UpdateReposAsync(IEnumerable<Repo> repos, Action<string> statusText, Action<string> progressText, bool downloadAnyways = false)
    {
        statusText.Invoke("Downloading updates...");
        
        List<Asset?> assets = [];
        bool downloadsOk = true;
        // Keep an in-flight batch stable while the desktop list is reordered.
        foreach (Repo repo in repos.ToArray())
        {
            if (repo.ExcludedFromDownloadAll || (!downloadAnyways && repo.Tag == repo.CurrentInstallTag))
            {
                continue;
            }
            
            Asset? asset = await DownloadAsset(repo, statusText, progressText, downloadAnyways);
            if (asset == null) downloadsOk = false;
            assets.Add(asset);
        }

        bool installed = await UpdateReposAsync(assets, statusText, progressText);
        return downloadsOk && installed;
    }

    internal static async Task<bool> UpdateReposAsync(List<Asset?> assets, Action<string> statusText, Action<string> progressText)
    {
        Logger.LogI("Updating repos");
        
        List<Asset> debs = [];
        List<Asset> exes = [];
        List<Asset> appImages = [];
        bool archivesOk = true;
        
        foreach (Asset? asset in assets)
        {
            if (asset == null)
            {
                continue;
            }
            
            if (asset.Value.Repo.AutoExtract && ArchiveExtractor.IsSupportedArchive(asset.Value.TempAssetPath))
            {
                statusText.Invoke($"Extracting {asset.Value.Repo.Name}");
                try
                {
                    await ArchiveExtractor.ExtractDownloadAsync(asset.Value.Repo, asset.Value.TempAssetPath,
                        asset.Value.Tag, progressText);
                }
                catch (Exception exception)
                {
                    archivesOk = false;
                    string message = $"Extraction failed ({exception.GetType().Name})";
                    Logger.LogE(message);
                    progressText.Invoke(message);
                }
                finally
                {
                    if (asset.Value.TemporaryDirectory is string directory && Directory.Exists(directory))
                        Directory.Delete(directory, recursive: true);
                }
                continue;
            }

            if (OperatingSystem.IsLinux() && asset.Value.TempAssetPath.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
            {
                debs.Add(asset.Value);
                if (!asset.Value.Repo.SaveFileAnyway) continue;
            }
            else if (OperatingSystem.IsLinux() && asset.Value.TempAssetPath.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase))
            {
                appImages.Add(asset.Value);
                if (!asset.Value.Repo.SaveFileAnyway) continue;
            }
            else if (asset.Value.TempAssetPath.EndsWith(".exe") ||
                     asset.Value.TempAssetPath.EndsWith(".msi"))
            {
                exes.Add(asset.Value);
                if (!asset.Value.Repo.SaveFileAnyway) continue;
            }

            statusText.Invoke($"Move file {asset.Value.Repo.Name}");
            CopyFile(asset.Value);
            if (!debs.Contains(asset.Value) && !exes.Contains(asset.Value) && !appImages.Contains(asset.Value))
                asset.Value.Repo.CurrentInstallTag = asset.Value.Tag;
        }
        
        statusText.Invoke("Installing Updates...");
        
        await HandleAppImagesAsync(appImages);
        foreach (Asset asset in appImages)
        {
            asset.Repo.InstalledAppImageId = InstallationManager.GetAppImageId(asset.Repo);
            asset.Repo.CurrentInstallTag = asset.Tag;
        }
        bool debsOk = true;
        Dictionary<Repo, string> debianPackages = new();
        InstallationManager installations = new();
        try
        {
            foreach (Asset asset in debs)
                debianPackages[asset.Repo] = await installations.ReadDebianPackageNameAsync(asset.TempAssetPath);
        }
        catch (Exception exception)
        {
            debsOk = false;
            Logger.LogE($"Debian package metadata could not be read ({exception.GetType().Name})");
            progressText("Could not read the Debian package metadata.");
        }
        if (debsOk) debsOk = await InstallDebsAsync(debs.Select(asset => asset.TempAssetPath).ToList(), progressText);
        if (debsOk)
        {
            foreach (Asset asset in debs)
            {
                asset.Repo.InstalledDebianPackage = debianPackages[asset.Repo];
                asset.Repo.CurrentInstallTag = asset.Tag;
            }
        }
        bool exesOk = await InstallExeAsync(exes.Select(asset => asset.TempAssetPath).ToList(), progressText);
        if (exesOk)
            foreach (Asset asset in exes) asset.Repo.CurrentInstallTag = asset.Tag;
        bool succeeded = archivesOk && debsOk && exesOk;
        if (succeeded) Logger.LogI("Repository updates completed");
        else Logger.LogE("Repository updates failed");
        return succeeded;
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
        string desktopFile = $"""
                             [Desktop Entry]
                             Name={asset.Repo.Name}
                             Comment={asset.Repo.Description}
                             GenericName={asset.Repo.Name}
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
        
    private static async Task<Asset?> DownloadAsset(Repo repo, Action<string> statusText, Action<string> progressText, bool downloadAnyways = false)
    {
        Logger.LogI("Preparing asset download");
        
        if (!downloadAnyways && repo.Tag == repo.CurrentInstallTag)
        {
            return null;
        }
        
        statusText.Invoke( $"Downloading {repo.Name}");
        
        Progress<double> progress = new(p =>
        {
            progressText.Invoke($"Downloaded: {p:0.00}%");
        });
        
        string downloadAssetName = repo.SelectedAssetName;
        string downloadAssetUrl = repo.SelectedAssetUrl;
        if (downloadAssetName == "" || downloadAssetUrl == "")
        {
            Logger.LogW("No release asset selected for download");
            statusText.Invoke($"No asset to download for {repo.Name}");
            return null;
        }

        string downloadTag = repo.Tag;
        string? temporaryDirectory = repo.AutoExtract && ArchiveExtractor.IsSupportedArchive(downloadAssetName)
            ? Path.Combine(FileManager.CachePath, $"archive-{Guid.NewGuid():N}")
            : null;
        string temporaryPath = Path.Combine(temporaryDirectory ?? FileManager.CachePath, downloadAssetName);
        if (temporaryDirectory != null) Directory.CreateDirectory(temporaryDirectory);
        try
        {
            await Api.DownloadFileAsync(downloadAssetUrl, temporaryPath, SecretsManager.LookupSecret("pat"), progress);
        }
        catch
        {
            if (temporaryDirectory != null) Directory.Delete(temporaryDirectory, recursive: true);
            throw;
        }

        Asset asset = new()
        {
            Repo = repo,
            TempAssetPath = temporaryPath,
            Tag = downloadTag,
            TemporaryDirectory = temporaryDirectory
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

        string installCommand = "apt-get install -y --allow-downgrades --reinstall ";
        foreach (string debPath in debPaths)
        {
            if (!debPath.Contains(".deb"))
            {
                continue;
            }
            installCommand += $"\"{debPath}\" ";
        }

        if (installCommand == "apt-get install -y --allow-downgrades --reinstall ")
        {
            return true;
        }

        bool usePkexec = CurPlatform == Platform.Avalonia;
        bool providePassword = !usePkexec && !string.IsNullOrEmpty(RootPassword);

        Logger.LogI($"Installing Debian packages (count {debPaths.Count})");
        progressText("Installing Debian packages...");
        
        using Process process = new()
        {
            StartInfo = new()
            {
                FileName = "/usr/bin/" + (usePkexec ? "pkexec" : "sudo"),
                Arguments = providePassword ? $"-S -p \"\" {installCommand}" : installCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = providePassword,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        
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
