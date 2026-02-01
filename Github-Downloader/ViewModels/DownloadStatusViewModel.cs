using Github_Downloader.ViewModels;

public class DownloadStatusViewModel : ViewModelBase
{
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
}