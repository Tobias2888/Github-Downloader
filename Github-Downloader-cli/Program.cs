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
        // Reading history must not initialize repositories, contact GitHub, or clear a shared cache.
        if (args.Length > 0 && args[0] == "history")
        {
            Environment.ExitCode = ArgHistory.Execute(args, Console.Out, Console.Error);
            return;
        }
        // Hand off before loading CLI state, which also clears the shared download cache.
        if (args.Length > 0 && args[0] == "--gui")
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: ghd --gui");
                Environment.ExitCode = 1;
                return;
            }

            Environment.ExitCode = DesktopLauncher.Launch();
            return;
        }

        Logger.LogDir = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader", "logs");
        Logger.LogToTerminal = false;
        Logger.LogFirstChance = false;
        Logger.LogDebug = false;
        Logger.LogLevel = LogLevel.Info;
        await Logger.CreateFile();
        Logger.LogI("Mode: cli");
        
        if (!SecretsManager.Initialized)
        {
            SecretsManager.Initialize("hofinga.gh-downloader.secret");
        }
        
        UpdateManager.CurPlatform = Platform.Terminal;
        
        await FileManager.LoadRepos();

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
                Logger.LogI("CLI action: list");
                ArgList.Execute(args);
                break;
            }

            case "add":
            {
                Logger.LogI("CLI action: add");
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
                Logger.LogI("CLI action: check");
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
                UpdateManager.Repos.ToList().ForEach(repo =>
                {
                    if (!repo.IsUpToDate)
                    {
                        count++;
                    }
                });
                Console.WriteLine($"{count} updates available execute {AppInfo.CliName} list to view details");
                break;
            }

            case "update":
            {
                Logger.LogI("CLI action: update");
                if (args.Length <= 1)
                {
                    return;
                }

                if (args[1] == "--all")
                {
                    await UpdateManager.SearchForUpdates(UpdateManager.Repos, Console.WriteLine);
                    await UpdateManager.UpdateReposAsync(UpdateManager.Repos,
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
                Logger.LogI("CLI action: reinstall");
                if (args.Length <= 1)
                {
                    return;
                }

                if (args[1] == "--all")
                {
                    await UpdateManager.SearchForUpdates(UpdateManager.Repos, Console.WriteLine);
                    await UpdateManager.UpdateReposAsync(UpdateManager.Repos,
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
                Logger.LogI("CLI action: repo");
                ArgRepo.Execute(args);
                break;
            }

            case "remove":
            {
                Logger.LogI("CLI action: remove");
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
                        
                        Logger.LogI("Saving personal access token");
                        SecretsManager.StoreSecret("pat", args[2]);
                        break;
                    
                    case "remove":
                        Logger.LogI("Removing personal access token");
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
        Console.WriteLine($$"""

                           Command:
                            {{AppInfo.CliName}} [list of arguments]
                            
                           Arguments:
                            -h / --help - Show this menu
                            -v / --version - Show installed version
                            --gui - Open the desktop application
                            history [--repo name] [--from date] [--to date] [--status result] [--action action] [--include-skipped] [--json] - Read history
                            history clear --yes - Clear completed history (keeps running entries)
                            
                            list repos - List all tracked repositories
                            list assets (repo id) - List all available assets of a specific repo
                            list versions (repo id) - List all available versions of a specific repo
                            repo (repo id) - Show repository-details about specific repository
                            repo (repo id) set-asset (asset id) - Select specific asset to download from repository by id
                            repo (repo id) set-asset-rule (pattern) - Save a case-sensitive glob (*, ?, and {x64,amd64} alternatives); quote the pattern
                            repo (repo id) clear-asset-rule - Return to the saved manual filename
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
