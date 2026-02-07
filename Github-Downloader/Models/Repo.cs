using System.Collections.Generic;
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
    public required List<string> AssetNames { get; set; }
    public required List<string> DownloadUrls { get; set; }
    private string _tag = string.Empty;
    public required string Tag
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
            if (_currentInstallTag == value)
            {
                return;
            }
            
            _currentInstallTag = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentInstallTag)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUpToDate)));
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

    public bool ExcludedFromDownloadAll { get; set; } = false;
    
    public event PropertyChangedEventHandler? PropertyChanged;
}