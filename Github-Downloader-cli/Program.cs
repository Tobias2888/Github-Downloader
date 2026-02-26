using System.Globalization;
using FileLib;
using Github_Downloader;
using Github_Downloader.Enums;
using LoggerLib;

namespace Github_Downloader_cli;

public static class Program
{
    private static string cliName = "gdh";
    
    static async Task Main(string[] args)
    {
        Logger.LogDir = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader", "logs");
        Logger.CreateFile();

        UpdateManager.CurPlatform = Platform.Terminal;
        
        await FileManager.LoadRepos(statusText =>
        {
            Console.WriteLine(statusText);
        });

        if (args.Length == 0)
        {
            ShowHelpPage();
            return;
        }
        
        switch (args[0])
        {
            case "-h":
            case "--help":
                ShowHelpPage();
                break;
            
            case "list":
                if (args.Length <= 1)
                {
                    return;
                }

                switch (args[1])
                {
                    case "repos":
                        for (int i = 0; i < UpdateManager.Repos.Count; i++)
                        {
                            Console.WriteLine($"{i} - {UpdateManager.Repos[i]}");
                        }
                        break;
                    
                    case "assets":
                        if (args.Length <= 2)
                        {
                            Console.WriteLine("\nSpecify repository id:\n\n" +
                                              $"{cliName} list assets (repo id)\n");
                            return;
                        }

                        int repoId = int.Parse(args[2]);

                        if (repoId < 0 || repoId >= UpdateManager.Repos.Count)
                        {
                            Console.WriteLine($"repo id {repoId} out of range");
                            return;
                        }
                        
                        for (int i = 0; i < UpdateManager.Repos[repoId].AssetNames.Count; i++)
                        {
                            string assetName = UpdateManager.Repos[repoId].AssetNames[i];
                            if (i == UpdateManager.Repos[repoId].DownloadAssetIndex)
                            {
                                Console.Write("\x1b[32m");
                            }
                            Console.WriteLine(i + " - " + assetName);
                            if (i == UpdateManager.Repos[repoId].DownloadAssetIndex)
                            {
                                Console.Write("\x1b[0m");
                            }
                        }
                        break;
                    
                    default:
                        Console.WriteLine("Not a valid command");
                        break;
                }
                break;
            
            case "check":
                await UpdateManager.SearchForUpdates(UpdateManager.Repos, Console.WriteLine);
                int count = 0;
                UpdateManager.Repos.ForEach(repo =>
                {
                    if (repo.Tag != repo.CurrentInstallTag)
                    {
                        count++; 
                    }
                });
                Console.WriteLine($"{count} updates available execute {cliName} list to view details");
                break;
            
            case "update":
                if (args.Length <= 1)
                {
                    return;
                }

                if (args[1] == "--all")
                {
                    await UpdateManager.UpdateRepos(UpdateManager.Repos, statusText =>
                    {
                        Console.WriteLine("statusText: " + statusText);
                    }, progressText =>
                    {
                        Console.WriteLine("progressText: " + progressText);
                    });
                    FileManager.SaveRepos();
                }
                break;
            
            case "repo":
                if (args.Length <= 1)
                {
                    Console.WriteLine("\nSpecify repository id:\n\n" +
                                      $"{cliName} repo (repo id)\n");
                    return;
                }

                if (args.Length <= 2)
                {
                    Console.WriteLine("test1");
                    return;
                }

                switch (args[2])
                {
                    case "set-asset":
                        if (args.Length <= 3)
                        {
                            Console.WriteLine("\nSpecify asset id:\n\n" +
                                              $"{cliName} repo set-asset (asset id)\n");
                            return;
                        }
                        
                        int assetId = int.Parse(args[3]);
                        if (assetId < 0 || assetId > UpdateManager.Repos.Count)
                        {
                            Console.WriteLine($"asset id {assetId} out of range");
                            return;
                        }
                        UpdateManager.Repos[int.Parse(args[1])].DownloadAssetIndex = int.Parse(args[3]);
                        FileManager.SaveRepos();
                        break;
                }
                break;
        }
    }

    private static void ShowHelpPage()
    {
        Console.WriteLine($"""

                           Command:
                            {cliName} [list of arguments]
                            
                           Arguments:
                            -h / --help - Show this menu
                            
                            list repos - List all tracked repositories
                            list assets (repo id) - List all available assets of a specific repo
                            check - Check for available updates
                            update --all - Update all repositories if updates available

                           """);
    }
}