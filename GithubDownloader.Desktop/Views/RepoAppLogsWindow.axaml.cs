using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GithubDownloader.Desktop.Services;

namespace GithubDownloader.Desktop.Views;

public partial class RepoAppLogsWindow : Window
{
    public RepoAppLogsWindow(RepoAppLogSession session)
    {
        InitializeComponent();
        DataContext = session;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();
}
