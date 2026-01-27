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

    [JsonIgnore]
    public bool IsUpToDate => Tag == CurrentInstallTag;
    public string DownloadPath { get; set; } = DirectoryHelper.GetUserDirPath();
    public bool ExcludedFromDownloadAll { get; set; } = false;
    
    public event PropertyChangedEventHandler? PropertyChanged;
}