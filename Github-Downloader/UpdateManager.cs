using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using FileLib;
using Github_Downloader.ViewModels;

namespace Github_Downloader;

public static class UpdateManager
{
    private static readonly string CachePath = Path.Join(DirectoryHelper.GetCacheDirPath(), "github-downloader");
    private static readonly string AppImagesPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader", "app-images");
    public static Window? Owner;
    
    private static readonly DownloadStatusViewModel DownloadStatusViewModel = App.DownloadStatusViewModel;
    private static readonly MainViewModel MainViewModel = ((App)Application.Current!).MainViewModel;

    private static DownloadStatus? _downloadStatus;
    
    private readonly record struct Asset(Repo Repo, string TempAssetPath);

    public static bool ShowDialog()
    {
        DownloadStatusViewModel.IsUpdating  = true;
        
        if (Owner is null) return false;
        if (!Owner.IsVisible) return false;
        
        _downloadStatus = new()
        {
            DataContext = DownloadStatusViewModel
        };
        _ = _downloadStatus.ShowDialog(Owner);
        return true;
    }

    private static void CloseDialog(bool shown)
    {
        DownloadStatusViewModel.IsUpdating = false;
        if (!shown) return;
        DownloadStatusViewModel.ProgressText = "";
        _downloadStatus?.Close();
    }

    public static async Task UpdateRepoDetails(List<Repo> repos)
    {
        if (DownloadStatusViewModel.IsUpdating) return;
        
        foreach (Repo repo in repos)
        {
            HttpResponseMessage httpRepoResponse = await Api.GetRequest(repo.Url.Replace("/releases/latest", ""), FileManager.GetPat());
            if (httpRepoResponse == null || !httpRepoResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed to fetch repo");
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

    public static async Task SearchForUpdates(List<Repo> repos)
    {
        if (DownloadStatusViewModel.IsUpdating) return;

        bool shown = ShowDialog();
        foreach (Repo repo in repos)
        {
            DownloadStatusViewModel.StatusText = $"Checking for {repo.Name}";
            await SearchForUpdates(repo, true);
        }
        CloseDialog(shown);

        foreach (Repo repo in repos)
        {
            if (repo.Tag == repo.CurrentInstallTag) continue;
            MainViewModel.HasUpdates = true;
            return;
        }
        
        MainViewModel.HasUpdates = false;
    }

    public static async Task SearchForUpdates(Repo repo, bool multiDownload = false)
    {
        bool shown = false;
        if (!multiDownload)
        {
            DownloadStatusViewModel.StatusText = $"Checking for {repo.Name}";
            shown = ShowDialog();
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
        
        HttpResponseMessage httpResponse = await Api.GetRequest(responseUrl, FileManager.GetPat());
        if (!httpResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to fetch release of: {responseUrl}");
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
        HttpResponseMessage httpResponseTags = await Api.GetRequest(tagsUrl, FileManager.GetPat());
        if (!httpResponseTags.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to fetch tags of: {tagsUrl}");
            return;
        }
        
        List<TagsResponse> tagsResponse = JsonSerializer.Deserialize<List<TagsResponse>>(await httpResponseTags.Content.ReadAsStringAsync());
        if (tagsResponse != null)
        {
            List<string> tags = ["latest"];
            tags.AddRange(tagsResponse.Select(tag => tag.name).ToList());
            repo.Tags = tags;
        }
        
        if (!multiDownload) CloseDialog(shown);
    }

    public static async Task UpdateRepo(Repo repo, bool downloadAnyways = false)
    {
        if (DownloadStatusViewModel.IsUpdating) return;

        ShowDialog();
        UpdateRepos([await DownloadAsset(repo, downloadAnyways)]);
    }

    public static async Task UpdateRepos(List<Repo> repos)
    {
        if (DownloadStatusViewModel.IsUpdating) return;

        ShowDialog();
        DownloadStatusViewModel.StatusText = "Downloading updates...";
        
        List<Asset?> assets = [];
        foreach (var repo in repos)
        {
            if (repo.ExcludedFromDownloadAll)
            {
                continue;
            }
            
            Asset? asset = await DownloadAsset(repo);
            assets.Add(asset);
        }

        UpdateRepos(assets);
    }

    private static void UpdateRepos(List<Asset?> assets)
    {
        List<string> debs = [];
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

            DownloadStatusViewModel.StatusText = $"Move file {asset.Value.Repo.Name}";
            CopyFile(asset.Value);
        }
        
        DownloadStatusViewModel.StatusText = "Installing Updates...";
        
        HandleAppImages(appImages);
        InstallDebs(debs);
        
        CloseDialog(true);
        MainViewModel.HasUpdates = false;
    }

    private static void HandleAppImages(List<Asset> assets)
    {
        foreach (Asset asset in assets)
        {
            string assetPath = Path.Join(AppImagesPath, asset.Repo.Name.Replace('/', '-'));
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
                    WorkingDirectory = CachePath,
                    UseShellExecute = false
                }
            };
            appImageExtract.Start();
            appImageExtract.WaitForExit();

            string tempIconPath = Path.Combine(CachePath, "squashfs-root", ".DirIcon");
            do
            {
                FileInfo fileInfo = new(tempIconPath);
                if (fileInfo.LinkTarget != null)
                {
                    tempIconPath = Path.Join(CachePath, "squashfs-root", fileInfo.LinkTarget);
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
        
    private static async Task<Asset?> DownloadAsset(Repo repo, bool downloadAnyways = false)
    {
        Console.WriteLine($"{repo.Tag} -> {repo.CurrentInstallTag}");
        if (!downloadAnyways && repo.Tag == repo.CurrentInstallTag)
        {
            return null;
        }
        
        DownloadStatusViewModel.StatusText = $"Downloading {repo.Name}";
        
        Progress<double> progress = new(p =>
        {
            DownloadStatusViewModel.ProgressText = $"Downloaded: {p:0.00}%";
        });
        
        string downloadAssetName = repo.AssetNames[repo.DownloadAssetIndex];
        await Api.DownloadFileAsync(repo.DownloadUrls[repo.DownloadAssetIndex], Path.Join(CachePath, downloadAssetName), FileManager.GetPat(), progress);

        repo.CurrentInstallTag = repo.Tag;
        
        Asset asset = new()
        {
            Repo = repo,
            TempAssetPath = Path.Join(CachePath, downloadAssetName)
        };

        Console.WriteLine($"asset: {asset}");
        
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

    private static void InstallDebs(List<string> debPaths)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }
        
        string installCommand = "pkexec apt-get install -y --allow-downgrades ";
        foreach (string debPath in debPaths)
        {
            if (!debPath.Contains(".deb"))
            {
                continue;
            }
            installCommand += $"\"{debPath}\" ";
        }

        if (installCommand == "pkexec apt-get install -y --allow-downgrades ")
        {
            return;
        }

        Console.WriteLine(installCommand);
        
        Process process = new()
        {
            StartInfo = new()
            {
                FileName = "pkexec",
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
            Console.WriteLine(args.Data);
            DownloadStatusViewModel.ProgressText += args.Data + "\n";
        };

        process.Start();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.WaitForExit();
    }
}