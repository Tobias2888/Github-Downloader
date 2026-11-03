using Avalonia.Threading;
using Github_Downloader.Enums;
using Github_Downloader.Models;
using LoggerLib;

namespace Github_Downloader.ViewModels;

public class MainViewModel : ViewModelBase
{
    private ViewModelBase _currentPage = new HomeViewModel();
    public ViewModelBase CurrentPage
    {
        get => _currentPage;
        set
        {
            _currentPage = value;
            OnPropertyChanged();
        }
    }

    public void SwitchPage(ViewNames viewName)
    {
        Logger.LogI($"Switch Page to {viewName}");
        CurrentPage = viewName switch
        {
            ViewNames.RepoDetails => new RepoDetailsViewModel(),
            ViewNames.Settings => new SettingsViewModel(),
            _ => new HomeViewModel()
        };
    }
    
    private bool _hasUpdates;

    public bool HasUpdates
    {
        get => _hasUpdates;
        set
        {
            if (_hasUpdates == value) return;
            _hasUpdates = value;
            OnPropertyChanged();
        }
    }
    
    private AppSettings _appSettings;

    public AppSettings AppSettings
    {
        get => _appSettings;
        set
        {
            if (_appSettings == value) return;
            _appSettings = value;
            OnPropertyChanged();
        }
    }
    
    public DispatcherTimer AutoCheckForUpdatesTimer { get; } = new();
}