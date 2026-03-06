using System.Diagnostics;
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
    public static List<Repo> Repos;
    public static Platform CurPlatform;
    
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
        
        HttpResponseMessage httpRepoResponse = await Api.GetRequest(repoUrl, SecretsManager.LookupSecret("pat"));
        if (httpRepoResponse == null || !httpRepoResponse.IsSuccessStatusCode)
        {
            Logger.LogE($"Failed to fetch repo: {repoUrl}");
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

    public static async Task UpdateRepoDetails(List<Repo> repos)
    {
        foreach (Repo repo in repos)
        {
            HttpResponseMessage httpRepoResponse = await Api.GetRequest(repo.Url.Replace("/releases/latest", ""), SecretsManager.LookupSecret("pat"));
            if (httpRepoResponse == null || !httpRepoResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed to fetch repo");
                Logger.LogW("Failed to fetch repo");
                if (httpRepoResponse != null)
                {
                    Logger.LogW(httpRepoResponse.StatusCode.ToString());
                    Logger.LogW(httpRepoResponse.ReasonPhrase);
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
        }
    }

    public static async Task SearchForUpdates(List<Repo> repos, Action<string> statusText)
    {
        foreach (Repo repo in repos)
        {
            statusText.Invoke($"Checking for {repo.Name}");
            await SearchForUpdates(repo, statusText, true);
        }

        foreach (Repo repo in repos)
        {
            if (repo.Tag == repo.CurrentInstallTag) continue;
            return;
        }
    }

    public static async Task SearchForUpdates(Repo repo, Action<string> statusText, bool multiDownload = false)
    {
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
            Logger.LogW($"Failed to fetch release of: {responseUrl}");
            return;
        }
        
        Response response = JsonSerializer.Deserialize<Response>(await httpResponse.Content.ReadAsStringAsync());
        if (response != null)
        {
            repo.AssetNames.Clear();
            foreach (Assets asset in response.assets)
            {
                repo.AssetNames.Add(asset.name);
            }
            
            repo.DownloadUrls = response.assets.ToList().Select(asset => asset.url).ToList();
            repo.LatestChangelog = response.body;
            repo.Tag = response.tag_name;
            repo.ReleaseDate = response.published_at;
        }

        string tagsUrl = $"https://api.github.com/repos/{repo.Name}/tags";
        HttpResponseMessage httpResponseTags = await Api.GetRequest(tagsUrl, SecretsManager.LookupSecret("pat"));
        if (!httpResponseTags.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to fetch tags of: {tagsUrl}");
            Logger.LogW($"Failed to fetch tags of: {tagsUrl}");
            return;
        }
        
        List<TagsResponse> tagsResponse = JsonSerializer.Deserialize<List<TagsResponse>>(await httpResponseTags.Content.ReadAsStringAsync());
        if (tagsResponse != null)
        {
            List<string> tags = ["latest"];
            tags.AddRange(tagsResponse.Select(tag => tag.name).ToList());
            repo.Tags = tags;
        }
    }

    public static async Task UpdateRepo(Repo repo, Action<string> statusText, Action<string> progressText, bool downloadAnyways = false)
    {
        UpdateRepos([await DownloadAsset(repo, statusText, progressText, downloadAnyways)], statusText, progressText);
    }

    public static async Task UpdateRepos(List<Repo> repos, Action<string> statusText, Action<string> progressText, bool downloadAnyways = false)
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

        UpdateRepos(assets, statusText, progressText);
    }

    private static void UpdateRepos(List<Asset?> assets, Action<string> statusText, Action<string> progressText)
    {
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
            else if (asset.Value.TempAssetPath.EndsWith(".exe"))
            {
                exes.Add(asset.Value.TempAssetPath);
                if (!asset.Value.Repo.SaveFileAnyway) continue;
            }

            statusText.Invoke($"Move file {asset.Value.Repo.Name}");
            CopyFile(asset.Value);
        }
        
        statusText.Invoke("Installing Updates...");
        
        HandleAppImages(appImages);
        InstallDebs(debs, progressText);
        InstallExe(exes, progressText);
    }

    private static void HandleAppImages(List<Asset> assets)
    {
        foreach (Asset asset in assets)
        {
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
                             Comment=Download {asset.Repo.Description}
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
        Logger.LogI($"Downloading asset {repo.Name}, {repo.Tag}");
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
        string destPath = Path.Join(asset.Repo.DownloadPath, asset.Repo.AssetNames[asset.Repo.DownloadAssetIndex]);
        if (File.Exists(destPath))
        {
            File.Delete(destPath);
        }
        
        File.Copy(Path.Join(asset.TempAssetPath), destPath);
    }

    private static void InstallDebs(List<string> debPaths, Action<string> progressText)
    {
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
        
        Logger.LogI(installCommand);
        process.OutputDataReceived += (_, args) =>
        {
            Logger.LogI(args.Data);
            progressText.Invoke(args.Data);
        };

        process.Start();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.WaitForExit();
    }

    private static void InstallExe(List<string> exePaths, Action<string> progressText)
    {
        foreach (string exePath in exePaths)
        {
            Logger.LogI($"Installing {exePath}");
            
            Process process = new()
            {
                StartInfo = new()
                    {
                    FileName = exePath,
                    UseShellExecute = true // Important to open GUI installer
                }
            };

            process.Start();

            process.WaitForExit();
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