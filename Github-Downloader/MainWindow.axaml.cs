using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FileLib;

namespace Github_Downloader;

public partial class MainWindow : Window
{
    private List<Repo> _repos;
    private string _appdataPath;
    private string _cachePath;
    private string _reposConfigFilePath;
    private string _token = "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";
    
    public MainWindow() => InitializeComponent();

    private async void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        _appdataPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader");
        _cachePath = Path.Join(DirectoryHelper.GetCacheDirPath(), "github-downloader");
        _reposConfigFilePath = Path.Join(_appdataPath, "repos.json");
        DirectoryHelper.CreateDir(_appdataPath);
        DirectoryHelper.CreateDir(_cachePath);
        
        

        if (File.Exists(_reposConfigFilePath))
        {
            string jsonString = File.ReadAllText(_reposConfigFilePath);
            _repos = JsonSerializer.Deserialize<List<Repo>>(jsonString);
        }
        else
        {
            _repos = new List<Repo>();
        }
        
        foreach (var repo in _repos)
        {
            CreateTrackedRepoEntry(repo.Name);
        }
    }

    private async void BtnAddRepo_OnClick(object? sender, RoutedEventArgs e)
    {
        string url;
        if (!string.IsNullOrEmpty(TbxUrl.Text))
        {
            try
            {
                string[] values = TbxUrl.Text.Split("github.com/");
                string[] values2 = values[1].TrimEnd('/').Split("/");
                url = $"https://api.github.com/repos/{values2[0]}/{values2[1]}/releases/latest";
            }
            catch (Exception ignored) {
                Console.WriteLine($"Failed to parse url: {TbxUrl.Text}");
                url = "";
            }
        }
        else
        {
            url = $"https://api.github.com/repos/{TbxOwner.Text}/{TbxRepo.Text}/releases/latest";
        }
        
        HttpResponseMessage httpResponse = await Api.GetRequest(url, _token);
        
        if (httpResponse == null || !httpResponse.IsSuccessStatusCode)
        {
            Console.WriteLine("Failed to fetch releases");
            ToastText.Text = $"Failed to fetch release of: {TbxUrl.Text}";
            ToastPopup.IsOpen = true;
            await Task.Delay(2500);
            ToastPopup.IsOpen = false;
            return;
        }
        
        Response response = JsonSerializer.Deserialize<Response>(await httpResponse.Content.ReadAsStringAsync());
        
        _repos.Add(new Repo
        {
            Url = url,
            DownloadUrl = response.assets[0].url,
            Name = response.name
        });

        SaveRepos();
        CreateTrackedRepoEntry(response.name);

        TbxUrl.Text = "";
    }
    
    private void SaveRepos()
    {
        if (!File.Exists(_reposConfigFilePath))
        {
            FileHelper.Create(_reposConfigFilePath);
        }

        string jsonString = JsonSerializer.Serialize(_repos, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        
        File.WriteAllText(_reposConfigFilePath, jsonString);
    }

    private void CreateTrackedRepoEntry(string name)
    {
        StackPanel stackPanel = new();
        stackPanel.Orientation = Orientation.Horizontal;

        Button btnUninstall = new();
        btnUninstall.Content = "Uninstall";
        btnUninstall.Background = Brushes.Red;

        Button btnUpdate = new();
        btnUpdate.Content = "Update";
        
        TextBlock textBlock = new();
        textBlock.Text = name;
        
        stackPanel.Children.Add(textBlock);
        stackPanel.Children.Add(btnUninstall);
        stackPanel.Children.Add(btnUpdate);
        
        TrackedRepos.Children.Add(stackPanel);
    }

    private async void BtnUpdateAll_OnClick(object? sender, RoutedEventArgs e)
    {
        List<string> debPaths = new();
        foreach (Repo repo in _repos)
        {
            HttpResponseMessage httpResponse = await Api.GetRequest(repo.Url, _token);
            if (!httpResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to fetch release of: {repo.Url}");
                continue;
            }
            
            Response response = JsonSerializer.Deserialize<Response>(await httpResponse.Content.ReadAsStringAsync());

            string downloadUrl = response.assets[0].url;

            await Api.DownloadFileAsync(repo.DownloadUrl, Path.Join(_cachePath, response.assets[0].name), _token);
            
            debPaths.Add(Path.Join(_cachePath, response.assets[0].name));
        }
        InstallDebs(debPaths);
    }

    public static void InstallDebs(List<string> debPaths)
    {
        string installCommand = "pkexec apt install -y ";
        foreach (string debPath in debPaths)
        {
            installCommand += $"\"{debPath}\" ";
        }

        Console.WriteLine(installCommand);
        
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pkexec",
                Arguments = installCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Installation failed:\n{error}");
        }

        Console.WriteLine(output);
    }
}