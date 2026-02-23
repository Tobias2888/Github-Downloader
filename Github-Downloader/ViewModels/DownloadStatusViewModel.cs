using Avalonia;
using Avalonia.Controls;

namespace Github_Downloader.ViewModels;

public class DownloadStatusViewModel : ViewModelBase
{
    //public Window? MainWindow { get; set; }
    private DownloadStatus? _downloadStatus;
    
    private string _statusText = "Checking for updates...";
    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }
    
    private string _progressText = "Downloading...";

    public string ProgressText
    {
        get => _progressText;
        set
        {
            _progressText = value;
            OnPropertyChanged();
        }
    }

    private bool _isUpdating;

    public bool IsUpdating
    {
        get => _isUpdating;
        set
        {
            _isUpdating = value;
            if (_isUpdating)
            {
                ShowDialog();
            }
            else
            {
                CloseDialog();
            }
            OnPropertyChanged();
        }
    }

    public bool ShowDialog()
    {
        Window mainWindow = ((App)Application.Current!).MainWindow;
        if (mainWindow is null) return false;
        if (!mainWindow.IsVisible) return false;

        _downloadStatus = new()
        {
            DataContext = this
        };
        _ = _downloadStatus.ShowDialog(mainWindow);
        return true;
    }

    public void CloseDialog()
    {
        ProgressText = "";
        _downloadStatus?.Close();
    }
}