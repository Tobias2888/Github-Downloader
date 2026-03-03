using Github_Downloader_lib;

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

        if (args.Length <= 2)
        {
            return;
        }

        int repoId = int.Parse(args[1]);

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
        }

    }
}