using Avalonia;
using Avalonia.Controls;
using Github_Downloader.Enums;
using Github_Downloader.ViewModels;

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

    private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.WindowClosing)
        {
            Hide();
            _mainViewModel.SwitchPage(ViewNames.Home);
            e.Cancel = true;
        }
    }
}