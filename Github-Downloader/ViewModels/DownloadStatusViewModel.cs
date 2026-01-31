using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Github_Downloader.ViewModels;

public sealed class DownloadStatusViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

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
    
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}