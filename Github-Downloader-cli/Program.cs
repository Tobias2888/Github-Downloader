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
        
        await FileManager.LoadRepos(Platform.Terminal, statusText =>
        {
            Console.WriteLine(statusText);
        });

        if (args.Length == 0)
        {
            return;
        }
        
        switch (args[0])
        {
            case "list":
                UpdateManager.Repos.ForEach(Console.WriteLine);
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
        }
    }
}