using FileLib;
using Github_Downloader_cli.Args;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Github_Downloader.Enums;
using LoggerLib;
using SecretsLib;

namespace Github_Downloader_cli;

public static class Program
{
    private static async Task Main(string[] args)
    {
        Logger.LogDir = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader", "logs");
        Logger.CreateFile();
        
        if (!SecretsManager.Initialized)
        {
            SecretsManager.Initialize("hofinga.gh-downloader.secret");
        }
        
        UpdateManager.CurPlatform = Platform.Terminal;
        
        await FileManager.LoadRepos(Console.WriteLine);

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
            {
                ArgList.Execute(args);
                break;
            }

            case "add":
            {
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
            }

            case "check":
            {
                int repoId = -1;
                if (args.Length > 1)
                {
                    repoId = int.Parse(args[1]);
                    
                    if (repoId < 0 || repoId >= UpdateManager.Repos.Count)
                    {
                        Console.WriteLine($"repo id {repoId} out of range");
                        return;
                    }
                }

                if (repoId < 0)
                {
                    await UpdateManager.SearchForUpdates(UpdateManager.Repos, Console.WriteLine);
                }
                else
                {
                    await UpdateManager.SearchForUpdates(UpdateManager.Repos[repoId], Console.WriteLine);
                }
                FileManager.SaveRepos();
                
                int count = 0;
                UpdateManager.Repos.ForEach(repo =>
                {
                    if (repo.Tag != repo.CurrentInstallTag)
                    {
                        count++;
                    }
                });
                Console.WriteLine($"{count} updates available execute {AppInfo.CliName} list to view details");
                break;
            }

            case "update":
            {
                if (args.Length <= 1)
                {
                    return;
                }

                if (args[1] == "--all")
                {
                    await UpdateManager.SearchForUpdates(UpdateManager.Repos, Console.WriteLine);
                    await UpdateManager.UpdateRepos(UpdateManager.Repos,
                        Console.WriteLine,
                        Console.WriteLine);
                    FileManager.SaveRepos();
                    return;
                }

                int repoId = int.Parse(args[1]);

                await UpdateManager.SearchForUpdates(UpdateManager.Repos[repoId], Console.WriteLine);
                await UpdateManager.UpdateRepo(UpdateManager.Repos[repoId],
                    Console.WriteLine,
                    Console.WriteLine);
                FileManager.SaveRepos();

                break;
            }
            
            case "reinstall":
            {
                if (args.Length <= 1)
                {
                    return;
                }

                if (args[1] == "--all")
                {
                    await UpdateManager.SearchForUpdates(UpdateManager.Repos, Console.WriteLine);
                    await UpdateManager.UpdateRepos(UpdateManager.Repos,
                        Console.WriteLine,
                        Console.WriteLine, 
                        true);
                    FileManager.SaveRepos();
                    return;
                }

                int repoId = int.Parse(args[1]);

                await UpdateManager.SearchForUpdates(UpdateManager.Repos[repoId], Console.WriteLine);
                await UpdateManager.UpdateRepo(UpdateManager.Repos[repoId],
                    Console.WriteLine,
                    Console.WriteLine,
                    true);
                FileManager.SaveRepos();

                break;
            }
                
            case "repo":
            {
                ArgRepo.Execute(args);
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
                            Console.WriteLine($"Specify personal access token: {AppInfo.CliName} pat set (personal access token)");
                            return;
                        }
                        
                        SecretsManager.StoreSecret("pat", args[3]);
                        break;
                    
                    case "remove":
                        SecretsManager.ClearSecret("pat");
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
                            {AppInfo.CliName} [list of arguments]
                            
                           Arguments:
                            -h / --help - Show this menu
                            -v / --version - Show installed version
                            
                            list repos - List all tracked repositories
                            list assets (repo id) - List all available assets of a specific repo
                            list versions (repo id) - List all available versions of a specific repo
                            repo (repo id) set-asset (asset id) - Select specific asset to download from repository by id
                            repo (repo id) set-version (version) - Select specific version to download from repository
                            repo (repo id) set-downloadpath (path) - Select specific download path
                            add (repo link) - Add a repository with github-link of repository
                            add add (publisher name) (repo name) - Add a repository with publisher-name and repository-name
                            remove (repo id) - Remove repository by id
                            check - Check for available updates
                            check (repo id) - Check for available updates of specific repository
                            update --all - Update all repositories if updates available
                            update (repo id) - Update specific repository by id
                            reinstall --all - Reinstall all repositories
                            reinstall (repo id) - Reinstall specific repository by id
                            pat set (pat) - Set a personal access token to access private repositories and increase rate-limit
                            pat remove - Remove personal access token

                           """);
    }
}