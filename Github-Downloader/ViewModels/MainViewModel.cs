using System.IO;
using FileLib;

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

    public MainViewModel()
    {
        AppdataPath = Path.Join(DirectoryHelper.GetAppDataDirPath(), "github-downloader");
        CachePath = Path.Join(DirectoryHelper.GetCacheDirPath(), "github-downloader");
        PatFilePath = Path.Join(AppdataPath, "pat");
        
        DirectoryHelper.CreateDir(AppdataPath);
        DirectoryHelper.CreateDir(CachePath);
    }
}