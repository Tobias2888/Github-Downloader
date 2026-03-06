using Github_Downloader_lib;
using Github_Downloader_lib.Models;

namespace Github_Downloader_cli.Args;

public static class ArgRepo
{
    public static void Execute(string[] args)
    {
        if (args.Length <= 1)
        {
            Console.WriteLine("\nSpecify repository id:\n\n" +
                              $"{AppInfo.CliName} repo (repo id)\n");
            return;
        }

        int repoId = int.Parse(args[1]);

        if (repoId < 0 || repoId >= UpdateManager.Repos.Count)
        {
            Console.WriteLine($"repo id {repoId} out of range");
        }
        
        if (args.Length <= 2)
        {
            Repo repo = UpdateManager.Repos[repoId];
            Console.WriteLine($"Name: {repo.Name}\n" +
                              "Version: " + (repo.Tag == repo.CurrentInstallTag
                                  ? repo.CurrentInstallTag
                                  : "\x1b[38;2;255;165;0m" + repo.CurrentInstallTag + " -> " + repo.Tag + "\x1b[0m") + "\n" +
                              $"Release Date: {repo.ReleaseDate}\n" +
                              $"Download Path: {repo.DownloadPath}\n" +
                              $"Selected Asset: {repo.AssetNames[repo.DownloadAssetIndex]}"
                              );
            return;
        }

        switch (args[2])
        {
            case "set-asset":
                if (args.Length <= 3)
                {
                    Console.WriteLine("\nSpecify asset id:\n\n" +
                                      $"{AppInfo.CliName} repo set-asset (asset id)\n");
                    return;
                }

                int assetId = int.Parse(args[3]);
                if (assetId < 0 || assetId > UpdateManager.Repos[repoId].AssetNames.Count)
                {
                    Console.WriteLine($"asset id {assetId} out of range");
                    return;
                }

                UpdateManager.Repos[repoId].DownloadAssetIndex = assetId;
                FileManager.SaveRepos();
                break;
                    
            case "set-version":
                if (args.Length <= 3)
                {
                    Console.WriteLine("\nSpecify version:\n\n" +
                                      $"{AppInfo.CliName} repo set-version (version)\n");
                    return;
                }
                        
                string version = args[3];
                if (!UpdateManager.Repos[repoId].Tags.Contains(version))
                {
                    Console.WriteLine($"Version {version} does not exist");
                    return;
                }

                UpdateManager.Repos[repoId].TargetTag = version;
                FileManager.SaveRepos();
                        
                break;
            
            case "set-downloadpath":
                if (args.Length <= 3)
                {
                    Console.WriteLine("\nSpecify download-path:\n\n" +
                                      $"{AppInfo.CliName} repo set-downloadpath (downloadpath)\n");
                    return;
                }
                
                string downloadpath = args[3];
                if (!Directory.Exists(downloadpath))
                {
                    Console.WriteLine($"Download path {downloadpath} does not exist");
                    return;
                }
                UpdateManager.Repos[repoId].DownloadPath = args[3];
                FileManager.SaveRepos();
                
                break;
        }

    }
}