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
    public static Window? Owner;
    
    private static readonly DownloadStatusViewModel DownloadStatusViewModel = ((App)Application.Current!).DownloadStatusViewModel;
    private static readonly MainViewModel MainViewModel = ((App)Application.Current!).MainViewModel;

    private static DownloadStatus? _downloadStatus;
    
    private readonly record struct Asset(Repo Repo, string TempAssetPath);

    private static void ShowDialog()
    {
        if (Owner is null) return;
        if (!Owner.IsVisible) return;
        
        _downloadStatus = new()
        {
            DataContext = DownloadStatusViewModel
        };
        _ = _downloadStatus.ShowDialog(Owner);
    }

    private static void CloseDialog()
    {
        DownloadStatusViewModel.ProgressText = "";
        _downloadStatus?.Close();
    }

    public static async Task SearchForUpdates(List<Repo> repos)
    {
        ShowDialog();
        foreach (Repo repo in repos)
        {
            DownloadStatusViewModel.StatusText = $"Checking for {repo.Name}";
            await SearchForUpdates(repo);
        }
        CloseDialog();

        foreach (Repo repo in repos)
        {
            if (repo.Tag == repo.CurrentInstallTag) continue;
            MainViewModel.HasUpdates = true;
            return;
        }
        
        MainViewModel.HasUpdates = false;
    }

    private static async Task SearchForUpdates(Repo repo)
    {
        HttpResponseMessage httpResponse = await Api.GetRequest(repo.Url, FileManager.GetPat());
        if (!httpResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to fetch release of: {repo.Url}");
            return;
        }
        
        Response response = JsonSerializer.Deserialize<Response>(await httpResponse.Content.ReadAsStringAsync());
        repo.DownloadUrls = response.assets.ToList().Select(asset => asset.url).ToList();
        repo.Name = response.name;
        repo.Tag = response.tag_name;
    }

    public static async Task UpdateRepo(Repo repo)
    {
        ShowDialog();
        UpdateRepos([await DownloadAsset(repo)]);
    }

    public static async Task UpdateRepos(List<Repo> repos)
    {
        ShowDialog();
        DownloadStatusViewModel.StatusText = "Downloading updates...";
        
        List<Asset?> assets = new();
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
        List<string> debs = new();
        foreach (Asset? asset in assets)
        {
            if (asset == null)
            {
                continue;
            }
            
            if (asset.Value.TempAssetPath.Contains(".deb"))
            {
                debs.Add(asset.Value.TempAssetPath);
            }
            else
            {
                DownloadStatusViewModel.StatusText = $"Move file {asset.Value.Repo.Name}";
                MoveFile(asset.Value);
            }
        }
        
        DownloadStatusViewModel.StatusText = "Installing Updates...";
        
        InstallDebs(debs);
        
        CloseDialog();
        MainViewModel.HasUpdates = false;
    }
    
    private static async Task<Asset?> DownloadAsset(Repo repo)
    {
        if (repo.Tag == repo.CurrentInstallTag)
        {
            return null;
        }
        
        DownloadStatusViewModel.StatusText = $"Downloading {repo.Name}";
        
        string downloadAssetName = repo.AssetNames[repo.DownloadAssetIndex];
        await Api.DownloadFileAsync(repo.DownloadUrls[repo.DownloadAssetIndex], Path.Join(CachePath, downloadAssetName), FileManager.GetPat());

        repo.CurrentInstallTag = repo.Tag;
        
        Asset asset = new()
        {
            Repo = repo,
            TempAssetPath = Path.Join(CachePath, downloadAssetName)
        };

        Console.WriteLine($"asset: {asset}");
        
        return asset;
    }

    private static void MoveFile(Asset asset)
    {
        string destPath = Path.Join(asset.Repo.DownloadPath, asset.Repo.AssetNames[asset.Repo.DownloadAssetIndex]);
        if (File.Exists(destPath))
        {
            File.Delete(destPath);
        }
        File.Move(Path.Join(asset.TempAssetPath), destPath);
    }

    private static void InstallDebs(List<string> debPaths)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }
        
        string installCommand = "pkexec apt-get install -y ";
        foreach (string debPath in debPaths)
        {
            if (!debPath.Contains(".deb"))
            {
                continue;
            }
            installCommand += $"\"{debPath}\" ";
        }

        if (installCommand == "pkexec apt-get install -y ")
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