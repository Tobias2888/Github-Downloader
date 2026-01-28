using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Github_Downloader;

public class UpdateManager
{
    public required string CachePath;
    
    private readonly record struct   Asset(Repo Repo, string TempAssetPath);

    public async Task SearchForUpdates(List<Repo> repos)
    {
        foreach (Repo repo in repos)
        {
            await SearchForUpdates(repo);
        }
    }

    public async Task SearchForUpdates(Repo repo)
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

    public async Task UpdateRepo(Repo repo)
    {
        UpdateRepos(new List<Asset?> { await DownloadAsset(repo) });
    }

    public async Task UpdateRepos(List<Repo> repos)
    {
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

    private void UpdateRepos(List<Asset?> assets)
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
                MoveFile(asset.Value);
            }
        }

        Console.WriteLine("assets: " + assets.Count);
        Console.WriteLine("debs: " + debs.Count);
        
        InstallDebs(debs);
    }
    
    private async Task<Asset?> DownloadAsset(Repo repo)
    {
        if (repo.Tag == repo.CurrentInstallTag)
        {
            Console.WriteLine(repo.Tag + " : " + repo.CurrentInstallTag);
            return null;
        }
        
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

    private void MoveFile(Asset asset)
    {
        string destPath = Path.Join(asset.Repo.DownloadPath, asset.Repo.AssetNames[asset.Repo.DownloadAssetIndex]);
        if (File.Exists(destPath))
        {
            File.Delete(destPath);
        }
        File.Move(Path.Join(asset.TempAssetPath), destPath);
    }

    private void InstallDebs(List<string> debPaths)
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
        
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pkexec",
                Arguments = installCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        Console.WriteLine(output);
        Console.WriteLine(error);
    }
}