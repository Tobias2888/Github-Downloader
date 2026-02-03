using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia;
using FileLib;
using Github_Downloader.ViewModels;

namespace Github_Downloader;

public static class FileManager
{
    private static readonly MainViewModel MainViewModel = ((App)Application.Current!).MainViewModel;
    
    private static readonly string ReposConfigFilePath = Path.Join(MainViewModel.AppdataPath, "repos.json");
    private static readonly string PatFilePath = Path.Join(MainViewModel.AppdataPath, "pat");
    
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