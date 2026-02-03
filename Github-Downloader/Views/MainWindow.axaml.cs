using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileLib;
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
            e.Cancel = true;
        }
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        
    }
}