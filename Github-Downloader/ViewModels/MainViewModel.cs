using System.IO;
using FileLib;
using Github_Downloader.Enums;

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
        CurrentPage = viewName switch
        {
            ViewNames.RepoDetails => new RepoDetailsViewModel(),
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
    
    public readonly string AppdataPath;
    public readonly string CachePath;
    public readonly string PatFilePath;
    public readonly string AppImagesPath;

    public MainViewModel()
    {
        AppdataPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader");
        CachePath = Path.Join(DirectoryHelper.GetCacheDirPath(), "github-downloader");
        PatFilePath = Path.Join(AppdataPath, "pat");
        AppImagesPath = Path.Join(AppdataPath, "app-images");
        
        DirectoryHelper.CreateDir(AppdataPath);
        DirectoryHelper.CreateDir(CachePath);
        DirectoryHelper.CreateDir(AppImagesPath);
    }
}