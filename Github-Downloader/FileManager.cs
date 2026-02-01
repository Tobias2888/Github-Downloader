using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FileLib;

namespace Github_Downloader;

public static class FileManager
{
    private static readonly string AppdataPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader");
    private static readonly string ReposConfigFilePath = Path.Join(AppdataPath, "repos.json");
    private static readonly string PatFilePath = Path.Join(AppdataPath, "pat");
    
    public static void SaveRepos(List<Repo> repos)
    {
        if (!File.Exists(ReposConfigFilePath))
        {
            FileHelper.Create(ReposConfigFilePath);
        }

        string jsonString = JsonSerializer.Serialize(repos, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        
        File.WriteAllText(ReposConfigFilePath, jsonString);
    }
    
    public static string GetPat()
    {
        return !File.Exists(PatFilePath) ? "" : File.ReadAllText(PatFilePath);
    }
}