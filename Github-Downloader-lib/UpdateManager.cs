using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
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

    public static void WatchRepos()
    {
        if (Repos == null) return;
        
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
        
        HttpResponseMessage httpRepoResponse = await Api.GetRequest(repoUrl, SecretsManager.LookupSecret("pat"));
        if (httpRepoResponse == null || !httpRepoResponse.IsSuccessStatusCode)
        {
            Logger.LogE($"Failed to fetch repo: {repoUrl}");
            Logger.LogE(httpRepoResponse.StatusCode.ToString());
            Logger.LogE(httpRepoResponse.ReasonPhrase);
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
            HttpResponseMessage httpRepoResponse = await Api.GetRequest(repo.Url.Replace("/releases/latest", ""), SecretsManager.LookupSecret("pat"));
            if (httpRepoResponse == null || !httpRepoResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed to fetch repo");
                Logger.LogE("Failed to fetch repo");
                if (httpRepoResponse != null)
                {
                    Logger.LogE(httpRepoResponse.StatusCode.ToString());
                    Logger.LogE(httpRepoResponse.ReasonPhrase);
                }
                return;
            }
        
            RepoResponse repoResponse = JsonSerializer.Deserialize<RepoResponse>(await httpRepoResponse.Content.ReadAsStringAsync());
            if (repoResponse == null)
            {
                return;
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

    public static async Task SearchForUpdates(Repo repo, Action<string> statusText, bool multiDownload = false)
    {
        Logger.LogI($"Checking for {repo.Name}");
        
        if (!multiDownload)
        {
            statusText.Invoke($"Checking for {repo.Name}");
        }

        string responseUrl;
        if (repo.TargetTag == "latest")
        {
            responseUrl = repo.Url;
        }
        else
        {
            responseUrl = $"https://api.github.com/repos/{repo.Name}/releases/tags/{repo.TargetTag}";
        }
        
        HttpResponseMessage httpResponse = await Api.GetRequest(responseUrl, SecretsManager.LookupSecret("pat"));
        if (!httpResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to fetch release of: {responseUrl}");
            Logger.LogE($"Failed to fetch release of: {responseUrl}");
            Logger.LogE(httpResponse.StatusCode.ToString());
            Logger.LogE(httpResponse.ReasonPhrase);
            return;
        }
        
        Response response = JsonSerializer.Deserialize<Response>(await httpResponse.Content.ReadAsStringAsync());
        if (response != null)
        {
            int oldIndex = repo.DownloadAssetIndex;
            repo.AssetNames.Clear();
            foreach (Assets asset in response.assets)
            {
                repo.AssetNames.Add(asset.name);
            }
            
            repo.DownloadUrls = response.assets.ToList().Select(asset => asset.url).ToList();
            repo.LatestChangelog = response.body;
            repo.Tag = response.tag_name;
            repo.ReleaseDate = response.published_at;

            if (oldIndex >= 0 && oldIndex < repo.AssetNames.Count)
            {
                repo.DownloadAssetIndex = oldIndex;
            }
        }

        string tagsUrl = $"https://api.github.com/repos/{repo.Name}/tags";
        HttpResponseMessage httpResponseTags = await Api.GetRequest(tagsUrl, SecretsManager.LookupSecret("pat"));
        if (!httpResponseTags.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to fetch tags of: {tagsUrl}");
            Logger.LogE($"Failed to fetch tags of: {tagsUrl}");
            Logger.LogE(httpResponseTags.StatusCode.ToString());
            Logger.LogE(httpResponseTags.ReasonPhrase);
            return;
        }
        
        List<TagsResponse> tagsResponse = JsonSerializer.Deserialize<List<TagsResponse>>(await httpResponseTags.Content.ReadAsStringAsync());
        if (tagsResponse != null)
        {
            List<string> tags = ["latest"];
            tags.AddRange(tagsResponse.Select(tag => tag.name).ToList());
            repo.Tags = tags;

            if (repo.TargetTag != "latest" && !repo.Tags.Contains(repo.TargetTag))
            {
                repo.TargetTag = "latest";
            }
        }
    }

    public static async Task UpdateRepo(Repo repo, Action<string> statusText, Action<string> progressText, bool downloadAnyways = false)
    {
        await UpdateReposAsync([await DownloadAsset(repo, statusText, progressText, downloadAnyways)], statusText, progressText);
    }

    public static async Task UpdateReposAsync(IEnumerable<Repo> repos, Action<string> statusText, Action<string> progressText, bool downloadAnyways = false)
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

        await UpdateReposAsync(assets, statusText, progressText);
    }

    private static async Task UpdateReposAsync(List<Asset?> assets, Action<string> statusText, Action<string> progressText)
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
        await InstallDebsAsync(debs, progressText);
        await InstallExeAsync(exes, progressText);
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
        
        string downloadAssetName = repo.AssetNames[repo.DownloadAssetIndex];
        await Api.DownloadFileAsync(repo.DownloadUrls[repo.DownloadAssetIndex], Path.Join(FileManager.CachePath, downloadAssetName), SecretsManager.LookupSecret("pat"), progress);

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
        string destName = asset.Repo.NewFileName == "" ? asset.Repo.AssetNames[asset.Repo.DownloadAssetIndex] : asset.Repo.NewFileName;
        string destPath = Path.Join(asset.Repo.DownloadPath, destName);
        if (File.Exists(destPath))
        {
            File.Delete(destPath);
        }
        
        File.Copy(Path.Join(asset.TempAssetPath), destPath);
    }

    private static async Task InstallDebsAsync(List<string> debPaths, Action<string> progressText)
    {
        Logger.LogI($"Installing debs: {debPaths}");
        
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
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
            return;
        }

        Logger.LogI($"Install command: {installCommand}");
        Logger.LogI("Using " + (CurPlatform == Platform.Avalonia ? "pkexec" : "sudo") + " for root");
        
        Process process = new()
        {
            StartInfo = new()
            {
                FileName = "/usr/bin/" + (CurPlatform == Platform.Avalonia ? "pkexec" : "sudo"),
                Arguments = installCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        
        process.OutputDataReceived += (_, args) =>
        {
            Logger.LogI(args.Data);
            progressText.Invoke(args.Data);
        };

        process.Start();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        
        if (process.ExitCode == 0)
        {
            Logger.LogI("Installation complete");
        }
        else
        {
            Logger.LogE($"Installation failed with exit code {process.ExitCode}");
        }
    }

    private static async Task InstallExeAsync(List<string> exePaths, Action<string> progressText)
    {
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
            }
        }
    }
}