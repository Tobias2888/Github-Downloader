using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Github_Downloader_lib;
using Github_Downloader.Enums;
using Github_Downloader.ViewModels;
using LoggerLib;

namespace Github_Downloader;

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
        ((Window)sender).WindowState = _mainViewModel.AppSettings.WindowState;
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