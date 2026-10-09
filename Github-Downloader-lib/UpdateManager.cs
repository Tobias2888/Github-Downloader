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
        foreach (var repo in Repos)
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
            nameof(Repo.NewFileName),
            nameof(Repo.CurrentInstallTag)
        };

        if (persistedProperties.Contains(e.PropertyName))
        {
            FileManager.SaveRepos();
        }
    }
    
    private readonly record struct Asset(Repo Repo, string TempAssetPath);

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
            Logger.LogE($"Failed to parse url: {repoUrl}");
            return null;
        }

        return await AddRepo(publisherName, repoName);
    }
    
    public static async Task<Repo?> AddRepo(string publisherName, string repoName)
    {
        string url = $"https://api.github.com/repos/{publisherName}/{repoName}/releases/latest";
        string repoUrl = $"https://api.github.com/repos/{publisherName}/{repoName}";
        
        Logger.LogI($"Adding repo: {repoUrl}");
        
        using HttpResponseMessage? httpRepoResponse = await Api.GetRequest(repoUrl, SecretsManager.LookupSecret("pat"));
        if (httpRepoResponse is not { IsSuccessStatusCode: true })
        {
            Logger.LogE($"Failed to fetch repo: {repoUrl}");
            if (httpRepoResponse != null)
            {
                Logger.LogE(httpRepoResponse.StatusCode.ToString());
                Logger.LogE(httpRepoResponse.ReasonPhrase);
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

        return repo;
    }

    public static async Task UpdateRepoDetails(IEnumerable<Repo> repos)
    {
        Logger.LogI("Updating repo-details");
        
        foreach (Repo repo in repos)
        {
            using HttpResponseMessage? httpRepoResponse = await Api.GetRequest(repo.Url.Replace("/releases/latest", ""), SecretsManager.LookupSecret("pat"));
            if (httpRepoResponse is not { IsSuccessStatusCode: true })
            {
                Console.WriteLine("Failed to fetch repo");
                Logger.LogE($"Failed to fetch repo: {repo.Name}");
                if (httpRepoResponse != null)
                {
                    Logger.LogE(httpRepoResponse.StatusCode.ToString());
                    Logger.LogE(httpRepoResponse.ReasonPhrase ?? "");
                }
                continue;
            }
        
            RepoResponse? repoResponse = JsonSerializer.Deserialize<RepoResponse>(await httpRepoResponse.Content.ReadAsStringAsync());
            if (repoResponse == null)
            {
                Logger.LogE($"Failed to parse repo: {repo.Name}");
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

    public static async Task SearchForUpdates(IEnumerable<Repo> repos, Action<string> statusText)
    {
        Logger.LogI("Searching for updates");
        
        foreach (Repo repo in repos)
        {
            statusText.Invoke($"Checking for {repo.Name}");
            await SearchForUpdates(repo, statusText, true);
        }
    }

    private enum ReleaseLookup
    {
        Found,
        NotFound,
        Failed
    }

    public static async Task SearchForUpdates(Repo repo, Action<string> statusText, bool multiDownload = false)
    {
        Logger.LogI($"Checking for {repo.Name}");
        
        if (!multiDownload)
        {
            statusText.Invoke($"Checking for {repo.Name}");
        }

        await UpdateTags(repo);

        if (repo.TargetTag != "latest" && !repo.Tags.Contains(repo.TargetTag))
        {
            repo.TargetTag = "latest";
        }

        (ReleaseLookup lookup, Response? response) = await GetRelease(repo, repo.TargetTag);
        if (lookup == ReleaseLookup.Failed)
        {
            return;
        }

        if (response == null)
        {
            Logger.LogE($"No release available for {repo.Name} ({repo.TargetTag})");
            repo.HasRelease = false;
            repo.AssetNames.Clear();
            repo.DownloadUrls = [];
            repo.Tag = "";
            repo.LatestChangelog = "";
            repo.ReleaseDate = "";
            return;
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
    }

    private static async Task UpdateTags(Repo repo)
    {
        string tagsUrl = $"https://api.github.com/repos/{repo.Name}/tags?per_page=100";
        using HttpResponseMessage? httpResponseTags = await Api.GetRequest(tagsUrl, SecretsManager.LookupSecret("pat"));
        if (httpResponseTags is not { IsSuccessStatusCode: true })
        {
            Logger.LogE($"Failed to fetch tags of: {tagsUrl}");
            Logger.LogE(httpResponseTags?.StatusCode.ToString() ?? "no response");
            Logger.LogE(httpResponseTags?.ReasonPhrase ?? "no response");
            return;
        }

        List<TagsResponse>? tagsResponse = JsonSerializer.Deserialize<List<TagsResponse>>(await httpResponseTags.Content.ReadAsStringAsync());
        if (tagsResponse == null)
        {
            return;
        }

        List<string> tags = ["latest"];
        tags.AddRange(tagsResponse.Select(tag => tag.name));
        repo.Tags = tags;
    }

    private static async Task<(ReleaseLookup Lookup, Response? Response)> GetRelease(Repo repo, string targetTag)
    {
        if (targetTag != "latest")
        {
            return await GetReleaseByUrl($"https://api.github.com/repos/{repo.Name}/releases/tags/{targetTag}");
        }

        (ReleaseLookup lookup, Response? response) = await GetReleaseByUrl(repo.Url);
        if (lookup == ReleaseLookup.Found)
        {
            return (lookup, response);
        }
        if (lookup == ReleaseLookup.Failed)
        {
            return (lookup, null);
        }

        List<Response>? releases = await GetReleaseList(repo);
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

    private static async Task<(ReleaseLookup Lookup, Response? Response)> GetReleaseByUrl(string url)
    {
        using HttpResponseMessage? httpResponse = await Api.GetRequest(url, SecretsManager.LookupSecret("pat"));
        if (httpResponse == null)
        {
            Logger.LogE($"No response for: {url}");
            return (ReleaseLookup.Failed, null);
        }

        if (httpResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return (ReleaseLookup.NotFound, null);
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            Logger.LogE($"Failed to fetch release of: {url}");
            Logger.LogE(httpResponse.StatusCode.ToString());
            Logger.LogE(httpResponse.ReasonPhrase ?? "");
            return (ReleaseLookup.Failed, null);
        }

        return (ReleaseLookup.Found,
            JsonSerializer.Deserialize<Response>(await httpResponse.Content.ReadAsStringAsync()));
    }

    private static async Task<List<Response>?> GetReleaseList(Repo repo)
    {
        string releasesUrl = $"https://api.github.com/repos/{repo.Name}/releases?per_page=100";
        using HttpResponseMessage? httpResponse = await Api.GetRequest(releasesUrl, SecretsManager.LookupSecret("pat"));
        if (httpResponse is not { IsSuccessStatusCode: true })
        {
            Logger.LogE($"Failed to fetch releases of: {releasesUrl}");
            Logger.LogE(httpResponse?.StatusCode.ToString() ?? "no response");
            Logger.LogE(httpResponse?.ReasonPhrase ?? "no response");
            return null;
        }

        return JsonSerializer.Deserialize<List<Response>>(await httpResponse.Content.ReadAsStringAsync());
    }

    public static async Task<bool> UpdateRepo(Repo repo, Action<string> statusText, Action<string> progressText, bool downloadAnyways = false)
    {
        return await UpdateReposAsync([await DownloadAsset(repo, statusText, progressText, downloadAnyways)], statusText, progressText);
    }

    public static async Task<bool> UpdateReposAsync(IEnumerable<Repo> repos, Action<string> statusText, Action<string> progressText, bool downloadAnyways = false)
    {
        statusText.Invoke("Downloading updates...");
        
        List<Asset?> assets = [];
        foreach (var repo in repos)
        {
            if (repo.ExcludedFromDownloadAll)
            {
                continue;
            }
            
            Asset? asset = await DownloadAsset(repo, statusText, progressText, downloadAnyways);
            assets.Add(asset);
        }

        return await UpdateReposAsync(assets, statusText, progressText);
    }

    private static async Task<bool> UpdateReposAsync(List<Asset?> assets, Action<string> statusText, Action<string> progressText)
    {
        Logger.LogI("Updating repos");
        
        List<string> debs = [];
        List<string> exes = [];
        List<Asset> appImages = [];
        
        foreach (Asset? asset in assets)
        {
            if (asset == null)
            {
                continue;
            }
            
            if (asset.Value.TempAssetPath.EndsWith(".deb"))
            {
                debs.Add(asset.Value.TempAssetPath);
                if (!asset.Value.Repo.SaveFileAnyway) continue;
            }
            else if (asset.Value.TempAssetPath.EndsWith(".AppImage"))
            {
                appImages.Add(asset.Value);
                if (!asset.Value.Repo.SaveFileAnyway) continue;
            }
            else if (asset.Value.TempAssetPath.EndsWith(".exe") ||
                     asset.Value.TempAssetPath.EndsWith(".msi"))
            {
                exes.Add(asset.Value.TempAssetPath);
                if (!asset.Value.Repo.SaveFileAnyway) continue;
            }

            statusText.Invoke($"Move file {asset.Value.Repo.Name}");
            CopyFile(asset.Value);
        }
        
        statusText.Invoke("Installing Updates...");
        
        HandleAppImages(appImages);
        bool debsOk = await InstallDebsAsync(debs, progressText);
        bool exesOk = await InstallExeAsync(exes, progressText);
        return debsOk && exesOk;
    }

    private static void HandleAppImages(List<Asset> assets)
    {
        foreach (Asset asset in assets)
        {
            Logger.LogI($"Installing AppImage: {asset.Repo.Name}");
            
            string assetPath = Path.Join(FileManager.AppImagesPath, asset.Repo.Name.Replace('/', '-'));
            DirectoryHelper.CreateDir(assetPath);
            string destPath = Path.Join(assetPath, asset.Repo.Name.Replace('/', '-') + ".AppImage");
            string iconPath = Path.Join(assetPath, "icon.png");
            File.Move(asset.TempAssetPath, destPath, overwrite: true);

            Process chmod = new()
            {
                StartInfo = new()
                {
                    FileName = "chmod",
                    ArgumentList = { "+x", destPath },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            chmod.Start();
            chmod.WaitForExit();
            
            Process appImageExtract = new()
            {
                StartInfo = new()
                {
                    FileName = destPath,
                    ArgumentList = { "--appimage-extract" },
                    WorkingDirectory = FileManager.CachePath,
                    UseShellExecute = false
                }
            };
            appImageExtract.Start();
            appImageExtract.WaitForExit();

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
        }
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
        string desktopFilePath = Path.Join(desktopDirectoryPath, asset.Repo.Name.Replace('/', '-') + ".desktop");
        DirectoryHelper.CreateDir(desktopDirectoryPath);
        FileHelper.Create(desktopFilePath);
        File.WriteAllText(desktopFilePath, desktopFile);
    }
        
    private static async Task<Asset?> DownloadAsset(Repo repo, Action<string> statusText, Action<string> progressText, bool downloadAnyways = false)
    {
        Logger.LogI($"Downloading asset: {repo.Name}, {repo.Tag}");
        
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
            Logger.LogE($"No asset selected to download for: {repo.Name}");
            statusText.Invoke($"No asset to download for {repo.Name}");
            return null;
        }

        await Api.DownloadFileAsync(downloadAssetUrl, Path.Join(FileManager.CachePath, downloadAssetName), SecretsManager.LookupSecret("pat"), progress);

        repo.CurrentInstallTag = repo.Tag;
        
        Asset asset = new()
        {
            Repo = repo,
            TempAssetPath = Path.Join(FileManager.CachePath, downloadAssetName)
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
        
        File.Copy(Path.Join(asset.TempAssetPath), destPath);
    }

    private static async Task<bool> InstallDebsAsync(List<string> debPaths, Action<string> progressText)
    {
        Logger.LogI($"Installing debs: {string.Join(", ", debPaths)}");
        
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return true;
        }

        string installCommand = "apt-get install -y --allow-downgrades --reinstall ";
        Console.WriteLine(installCommand);
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

        Logger.LogI($"Install command: {installCommand}");
        Logger.LogI("Using " + (usePkexec ? "pkexec" : "sudo") + " for root"
            + (providePassword ? " (password supplied)" : ""));
        
        Process process = new()
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
        
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null) return;
            Logger.LogI(args.Data);
            progressText.Invoke(args.Data);
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null) return;
            Logger.LogE(args.Data);
            progressText.Invoke(args.Data);
        };

        process.Start();

        if (providePassword)
        {
            await process.StandardInput.WriteLineAsync(RootPassword);
            process.StandardInput.Close();
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        
        if (process.ExitCode == 0)
        {
            Logger.LogI("Installation complete");
            return true;
        }

        string hint = providePassword && process.ExitCode == 1
            ? " (the supplied root password may be incorrect)"
            : "";
        Logger.LogE($"Installation failed with exit code {process.ExitCode}{hint}");
        return false;
    }

    private static async Task<bool> InstallExeAsync(List<string> exePaths, Action<string> progressText)
    {
        bool allOk = true;
        foreach (string exePath in exePaths)
        {
            Logger.LogI($"Installing exe: {exePath}");
            
            Process process = new()
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