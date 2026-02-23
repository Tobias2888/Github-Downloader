using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using FileLib;

namespace Github_Downloader;

public class Repo : INotifyPropertyChanged
{
    public required string Url { get; set; }
    
    private string _name = string.Empty;
    public required string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }
            
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public string Description { get; set; } = "No description available";
    public int DownloadAssetIndex { get; set; }

    private ObservableCollection<string> _assetNames = [];
    public ObservableCollection<string> AssetNames
    {
        get => _assetNames;
        set
        {
            if (_assetNames == value) return;
            _assetNames = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AssetNames)));
        }
    }
    
    public List<string> DownloadUrls { get; set; } = [];
    
    private string _tag = string.Empty;
    public string Tag
    {
        get => _tag;
        set
        {
            if (_tag == value)
            {
                return;
            }
            
            _tag = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tag)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUpToDate)));
        }
    }
    
    private string _currentInstallTag = string.Empty;
    public string CurrentInstallTag
    {
        get => _currentInstallTag;
        set
        {
            if (_currentInstallTag == value) return;
            
            _currentInstallTag = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentInstallTag)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUpToDate)));
        }
    }
    
    private string _targetTag = "latest";
    public string TargetTag
    {
        get => _targetTag;
        set
        {
            if (_targetTag == value) return;
            
            _targetTag = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetTag)));
        }
    }

    private List<string> _tags = ["latest"];
    public List<string> Tags
    {
        get => _tags;
        set
        {
            if (_tags == value) return;
            _tags = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tags)));
        }
    }
    
    private string _releaseDate = string.Empty;
    public string ReleaseDate
    {
        get => _releaseDate;
        set
        {
            if (_releaseDate == value) return;
            _releaseDate = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReleaseDate)));
        }
    }
    
    private string _githubLink = string.Empty;
    public string GitHubLink
    {
        get => _githubLink;
        set
        {
            if (_githubLink == value) return;
            _githubLink = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GitHubLink)));
        }
    }
    
    private string _latestChangelog = string.Empty;
    public string LatestChangelog
    {
        get => _latestChangelog;
        set
        {
            if (_latestChangelog == value) return;
            _latestChangelog = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LatestChangelog)));
        }
    }

    [JsonIgnore]
    public bool IsUpToDate => Tag == CurrentInstallTag;
    
    private string _downloadPath = DirectoryHelper.GetUserDirPath();
    public string DownloadPath
    {
        get => _downloadPath;
        set
        {
            if (_downloadPath == value) return;
            _downloadPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DownloadPath)));
        }
    }

    private bool _saveFileAnyway;
    public bool SaveFileAnyway
    {
        get => _saveFileAnyway;
        set
        {
            if (_saveFileAnyway == value) return;
            _saveFileAnyway = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SaveFileAnyway)));
        }
    }

    public bool ExcludedFromDownloadAll { get; set; } = false;
    
    public event PropertyChangedEventHandler? PropertyChanged;
}