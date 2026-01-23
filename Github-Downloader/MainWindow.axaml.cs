using System;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Github_Downloader;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        
    }

    private async void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        Repo repo = new Repo
        {
            Url = $"https://api.github.com/repos/{TbxOwner.Text}/{TbxRepo.Text}/releases/latest"
        };
        
        string responseJson = await Api.GetRequest(repo.Url);
        Response response = JsonSerializer.Deserialize<Response>(responseJson);

        TextBlock textBlock = new();
        textBlock.Text = response.name;
        TrackedRepos.Children.Add(textBlock);
    }
}