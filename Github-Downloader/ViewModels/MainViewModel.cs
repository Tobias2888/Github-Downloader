namespace Github_Downloader.ViewModels;

public class MainViewModel : ViewModelBase
{
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
}