using Github_Downloader_lib;

namespace Github_Downloader_cli.Args;

public static class ArgList
{
    public static void Execute(string[] args)
    {
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
            {
                if (args.Length <= 2)
                {
                    Console.WriteLine("\nSpecify repository id:\n\n" +
                                      $"{AppInfo.CliName} list assets (repo id)\n");
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
            }

            case "versions":
            {
                if (args.Length <= 2)
                {
                    Console.WriteLine("\nSpecify repository id:\n\n" +
                                      $"{AppInfo.CliName} list assets (repo id)\n");
                    return;
                }
                    
                int repoId = int.Parse(args[2]);

                if (repoId < 0 || repoId >= UpdateManager.Repos.Count)
                {
                    Console.WriteLine($"repo id {repoId} out of range");
                    return;
                }

                for (int i = UpdateManager.Repos[repoId].Tags.Count - 1; i >= 0; i--)
                {
                    string curTag = UpdateManager.Repos[repoId].Tags[i];
                    if (curTag == UpdateManager.Repos[repoId].TargetTag)
                    {
                        Console.Write("\x1b[32m");
                    }

                    Console.WriteLine(curTag);
                    if (curTag == UpdateManager.Repos[repoId].TargetTag)
                    {
                        Console.Write("\x1b[0m");
                    }
                }
                break;
            }

            default:
                Console.WriteLine("Not a valid command");
                break;
        }
    }
}