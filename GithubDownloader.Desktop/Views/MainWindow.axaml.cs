using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GithubDownloader.Core;
using GithubDownloader.Desktop.Enums;
using GithubDownloader.Desktop.ViewModels;
using LoggerLib;

namespace GithubDownloader.Desktop;

public partial class MainWindow : Window
{
    private MainViewModel _mainViewModel;
    
    public MainWindow()
    {
        InitializeComponent();
        _mainViewModel = ((App)Application.Current!).MainViewModel;
        DataContext = _mainViewModel;
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        ((Window)sender).Title = $"Github Downloader - {AppInfo.Version}";
        WindowState savedState = _mainViewModel.AppSettings.WindowState;
        ((Window)sender).WindowState = savedState == WindowState.Minimized ? WindowState.Normal : savedState;
    }

    private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.WindowClosing)
        {
            Logger.LogI("Main window closed to tray");
            Hide();
            _mainViewModel.SwitchPage(ViewNames.Home);
            e.Cancel = true;
        }
    }
}
