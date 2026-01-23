using System;
using System.Collections.Generic;
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
    private string _token = "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";
    
    public MainWindow() => InitializeComponent();

    private async void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        _appdataPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader");
        _cachePath = Path.Join(DirectoryHelper.GetCacheDirPath(), "github-downloader");
        DirectoryHelper.CreateDir(_appdataPath);
        DirectoryHelper.CreateDir(_cachePath);
        FileHelper.Create(Path.Join(_appdataPath, "repos.json"));
        
        _repos = new List<Repo>();
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
        });

        CreateTrackedRepoEntry(response);

        TbxUrl.Text = "";
    }

    private void CreateTrackedRepoEntry(Response response)
    {
        StackPanel stackPanel = new();
        stackPanel.Orientation = Orientation.Horizontal;

        Button btnUninstall = new();
        btnUninstall.Content = "Uninstall";
        btnUninstall.Background = Brushes.Red;
        
        TextBlock textBlock = new();
        textBlock.Text = response.name;
        
        stackPanel.Children.Add(textBlock);
        stackPanel.Children.Add(btnUninstall);
        
        TrackedRepos.Children.Add(stackPanel);
    }

    private async void BtnUpdateAll_OnClick(object? sender, RoutedEventArgs e)
    {
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
        }
    }
}