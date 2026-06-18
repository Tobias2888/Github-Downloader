using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using FileLib;

namespace Github_Downloader_lib.Models;

public class Repo : INotifyPropertyChanged
{
    private string _url = string.Empty;
    public required string Url 
    { 
        get => _url;
        set
        {
            if (_url == value) return;
            _url = value;
            OnPropertyChanged(nameof(Url));
        }
    }
    
    private string _name = string.Empty;
    public required string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged(nameof(Name));
        }
    }

    private string _description = "No description available";
    public string Description 
    { 
        get => _description;
        set
        {
            if (_description == value) return;
            _description = value;
            OnPropertyChanged(nameof(Description));
        }
    }

    private int _downloadAssetIndex;
    public int DownloadAssetIndex 
    { 
        get => _downloadAssetIndex;
        set
        {
            if (_downloadAssetIndex == value) return;
            _downloadAssetIndex = value;
            OnPropertyChanged(nameof(DownloadAssetIndex));
        }
    }

    private ObservableCollection<string> _assetNames = [];
    public ObservableCollection<string> AssetNames
    {
        get => _assetNames;
        set
        {
            if (_assetNames == value || value == null) return;
            _assetNames = value;
            OnPropertyChanged(nameof(AssetNames));
        }
    }
    
    private List<string> _downloadUrls = [];
    public List<string> DownloadUrls 
    { 
        get => _downloadUrls;
        set
        {
            if (_downloadUrls == value || value == null) return;
            _downloadUrls = value;
            OnPropertyChanged(nameof(DownloadUrls));
        }
    }
    
    private string _tag = string.Empty;
    public string Tag
    {
        get => _tag;
        set
        {
            if (_tag == value) return;
            _tag = value;
            OnPropertyChanged(nameof(Tag));
            OnPropertyChanged(nameof(IsUpToDate));
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
            OnPropertyChanged(nameof(CurrentInstallTag));
            OnPropertyChanged(nameof(IsUpToDate));
        }
    }
    
    private string _targetTag = "latest";
    public string TargetTag
    {
        get => _targetTag;
        set
        {
            if (_targetTag == value || value == null) return;
            _targetTag = value;
            OnPropertyChanged(nameof(TargetTag));
        }
    }

    private List<string> _tags = ["latest"];
    public List<string> Tags
    {
        get => _tags;
        set
        {
            if (_tags == value || value == null) return;
            _tags = value;
            OnPropertyChanged(nameof(Tags));
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
            OnPropertyChanged(nameof(ReleaseDate));
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
            OnPropertyChanged(nameof(GitHubLink));
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
            OnPropertyChanged(nameof(LatestChangelog));
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
            OnPropertyChanged(nameof(DownloadPath));
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
            OnPropertyChanged(nameof(SaveFileAnyway));
        }
    }
    
    private string _newFileName = string.Empty;
    public string NewFileName
    {
        get => _newFileName;
        set
        {
            if (_newFileName == value) return;
            _newFileName = value;
            OnPropertyChanged(nameof(NewFileName));
        }
    }

    private bool _excludedFromDownloadAll;
    public bool ExcludedFromDownloadAll 
    { 
        get => _excludedFromDownloadAll;
        set
        {
            if (_excludedFromDownloadAll == value) return;
            _excludedFromDownloadAll = value;
            OnPropertyChanged(nameof(ExcludedFromDownloadAll));
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    [JsonIgnore]
    public bool IsSelected
    {
        get => !ExcludedFromDownloadAll;
        set => ExcludedFromDownloadAll = !value;
    }

    public override string ToString()
    {
        return $"{Name} - " + (Tag == CurrentInstallTag ? CurrentInstallTag : "\x1b[38;2;255;165;0m" + CurrentInstallTag + " -> " + Tag + "\x1b[0m");
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
