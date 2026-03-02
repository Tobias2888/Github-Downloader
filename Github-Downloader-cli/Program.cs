using FileLib;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Github_Downloader.Enums;
using LoggerLib;

namespace Github_Downloader_cli;

public static class Program
{
    private const string CliName = "ghd";

    private static async Task Main(string[] args)
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
            
            case "-v":
            case "--version":
                Console.WriteLine(AppInfo.Version);
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
                                              $"{CliName} list assets (repo id)\n");
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

            case "add":
                Repo? repo;
                switch (args.Length)
                {
                    case 2:
                        string repoUrl = args[1];
                        repo = await UpdateManager.AddRepo(repoUrl);
                        break;

                    case 3:
                        string publisherName = args[1];
                        string repoName = args[2];
                        repo = await UpdateManager.AddRepo(publisherName, repoName);
                        break;

                    default:
                        Console.WriteLine("Invalid arguments");
                        return;
                }

                if (repo == null)
                {
                    Console.WriteLine("Failed to find repo");
                    return;
                }

                Console.WriteLine("Added repo");

                await UpdateManager.SearchForUpdates(repo, Console.WriteLine);
                
                UpdateManager.Repos.Add(repo);
                FileManager.SaveRepos();
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
                FileManager.SaveRepos();
                Console.WriteLine($"{count} updates available execute {CliName} list to view details");
                break;

            case "update":
            {
                if (args.Length <= 1)
                {
                    return;
                }

                if (args[1] == "--all")
                {
                    await UpdateManager.UpdateRepos(UpdateManager.Repos,
                        Console.WriteLine,
                        Console.WriteLine);
                    FileManager.SaveRepos();
                    return;
                }

                int repoId = int.Parse(args[1]);
                
                await UpdateManager.UpdateRepo(UpdateManager.Repos[repoId],
                    Console.WriteLine,
                    Console.WriteLine);
                FileManager.SaveRepos();

                break;
            }

            case "repo":
            {
                if (args.Length <= 1)
                {
                    Console.WriteLine("\nSpecify repository id:\n\n" +
                                      $"{CliName} repo (repo id)\n");
                    return;
                }

                if (args.Length <= 2)
                {
                    Console.WriteLine("test1");
                    return;
                }

                int repoId = int.Parse(args[1]);

                switch (args[2])
                {
                    case "set-asset":
                        if (args.Length <= 3)
                        {
                            Console.WriteLine("\nSpecify asset id:\n\n" +
                                              $"{CliName} repo set-asset (asset id)\n");
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
                }

                break;
            }

            case "remove":
            {
                if (args.Length <= 1)
                {
                    return;
                }

                int repoId = int.Parse(args[1]);

                if (repoId < 0 || repoId >= UpdateManager.Repos.Count)
                {
                    Console.WriteLine($"repo id {repoId} out of range");
                    return;
                }
                
                UpdateManager.Repos.RemoveAt(repoId);
                FileManager.SaveRepos();
                break;
            }
            
            case "pat":
                if (args.Length <= 1)
                {
                    return;
                }

                switch (args[1])
                {
                    case "set":
                        if (args.Length <= 2)
                        {
                            Console.WriteLine($"Specify personal access token: {CliName} pat set (personal access token)");
                            return;
                        }
                        
                        FileManager.SetPat(args[3]);
                        break;
                    
                    case "remove":
                        FileManager.SetPat("");
                        break;
                }
                break;
            
            default:
                Console.WriteLine("invalid arguments");
                break;
        }
    }

    private static void ShowHelpPage()
    {
        Console.WriteLine($"""

                           Command:
                            {CliName} [list of arguments]
                            
                           Arguments:
                            -h / --help - Show this menu
                            
                            list repos - List all tracked repositories
                            list assets (repo id) - List all available assets of a specific repo
                            repo (repo id) set-asset (asset id) - Select specific asset to download from repository by id
                            add (repo link) - Add a repository with github-link of repository
                            add add (publisher name) (repo name) - Add a repository with publisher-name and repository-name
                            remove (repo id) - Remove repository by id
                            check - Check for available updates
                            update --all - Update all repositories if updates available
                            update (repo id) - Update specific repository by id
                            pat set (pat) - Set a personal access token to access private repositories and increase rate-limit
                            pat remove - Remove personal access token

                           """);
    }
}