using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Github_Downloader.Enums;
using Github_Downloader.ViewModels;

namespace Github_Downloader.Views;

public partial class SettingsView : UserControl
{
    private readonly MainViewModel _mainViewModel;
    
    public SettingsView()
    {
        InitializeComponent();
        _mainViewModel = ((App) Application.Current).MainViewModel;
        DataContext = new SettingsViewModel();
    }

    private void ImgBack_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _mainViewModel.SwitchPage(ViewNames.Home);
    }
}