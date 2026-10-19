using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using FileLib;

namespace Github_Downloader_lib.Models;

public class Repo : INotifyPropertyChanged, IJsonOnDeserializing, IJsonOnDeserialized, IJsonOnSerializing
{
    public Repo()
    {
        AssetNames.CollectionChanged += AssetNamesOnCollectionChanged;
    }

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
            OnPropertyChanged(nameof(ExtractionDirectory));
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
    private bool _selectionInitialized;
    private bool _deserializing;
    private bool _updatingAssets;
    // Null means an old configuration has no saved filename yet; empty means deliberately unselected.
    private string? _assetName;
    private string _assetRule = string.Empty;

    // Saved separately from the index so releases may safely reorder their assets.
    public string AssetName
    {
        get => _assetName ?? string.Empty;
        set
        {
            _assetName = value ?? string.Empty;
            if (_deserializing) return;
            _selectionInitialized = true;
            OnSelectedAssetChanged();
            OnPropertyChanged(nameof(AssetName));
        }
    }

    public string AssetRule
    {
        get => _assetRule;
        set
        {
            value ??= string.Empty;
            if (_assetRule == value) return;
            if (!_deserializing) InitializeAssetSelection();
            _assetRule = value;
            if (_deserializing) return;
            OnSelectedAssetChanged();
            OnPropertyChanged(nameof(AssetRule));
        }
    }

    public int DownloadAssetIndex 
    { 
        get
        {
            if (!_selectionInitialized) return _downloadAssetIndex;
            int[] matches = Enumerable.Range(0, AssetNames.Count)
                .Where(index => HasAssetRule ? AssetSelection.Matches(AssetNames[index], AssetRule)
                    : AssetName.Length > 0 && AssetNames[index] == AssetName).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : -1;
        }
        set
        {
            if (_deserializing) { _downloadAssetIndex = value; return; }
            // ComboBox bindings may clear selection while their item source is replaced.
            if (_updatingAssets || value < 0 || value >= AssetNames.Count) return;
            if (_selectionInitialized && DownloadAssetIndex == value) return;
            _downloadAssetIndex = value;
            _assetName = AssetNames[value];
            _assetRule = string.Empty;
            _selectionInitialized = true;
            OnSelectedAssetChanged();
            OnPropertyChanged(nameof(AssetName));
            OnPropertyChanged(nameof(AssetRule));
        }
    }

    public void InitializeAssetSelection()
    {
        if (_selectionInitialized) return;
        if (_assetName == null && _downloadAssetIndex >= 0 && _downloadAssetIndex < AssetNames.Count)
            _assetName = AssetNames[_downloadAssetIndex];
        _assetName ??= string.Empty;
        _selectionInitialized = true;
    }

    public void ReplaceAssets(IEnumerable<string> names, IEnumerable<string> urls)
    {
        InitializeAssetSelection();
        _updatingAssets = true;
        try
        {
            _assetNames.CollectionChanged -= AssetNamesOnCollectionChanged;
            _assetNames = new ObservableCollection<string>(names);
            _assetNames.CollectionChanged += AssetNamesOnCollectionChanged;
            _downloadUrls = urls.ToList();
            OnPropertyChanged(nameof(AssetNames));
            OnPropertyChanged(nameof(DownloadUrls));
            OnPropertyChanged(nameof(HasAssets));
            OnSelectedAssetChanged();
        }
        finally { _updatingAssets = false; }
    }

    void IJsonOnDeserializing.OnDeserializing() => _deserializing = true;
    void IJsonOnDeserialized.OnDeserialized()
    {
        _deserializing = false;
        InitializeAssetSelection();
    }
    void IJsonOnSerializing.OnSerializing() => InitializeAssetSelection();

    [JsonIgnore]
    public bool HasAssetRule => AssetRule.Length > 0;

    [JsonIgnore]
    public List<string> AssetRuleMatches => HasAssetRule
        ? AssetSelection.FindMatches(AssetNames, AssetRule) : [];

    [JsonIgnore]
    public bool HasMultipleAssetRuleMatches => HasAssetRule && AssetRuleMatches.Count > 1;

    [JsonIgnore]
    public bool HasNoAssetRuleMatches => HasAssetRule && AssetRuleMatches.Count == 0;

    [JsonIgnore]
    public string AssetSelectionStatus => HasAssetRule
        ? AssetRuleMatches.Count switch
        {
            0 => "No assets match this rule. Download blocked; change the rule or select a file manually.",
            1 => $"Rule matches: {SelectedAssetName}",
            int count => $"{count} assets match this rule. Download blocked; make the rule more specific."
        }
        : SelectedAssetName.Length > 0 ? $"Selected: {SelectedAssetName}"
        : AssetName.Length > 0 ? $"Saved asset '{AssetName}' is missing. Select a file or set a rule."
        : "Select a file or set an asset rule before downloading.";

    private ObservableCollection<string> _assetNames = [];
    public ObservableCollection<string> AssetNames
    {
        get => _assetNames;
        set
        {
            if (_assetNames == value || value == null) return;
            _assetNames.CollectionChanged -= AssetNamesOnCollectionChanged;
            _assetNames = value;
            _assetNames.CollectionChanged += AssetNamesOnCollectionChanged;
            OnPropertyChanged(nameof(AssetNames));
            OnPropertyChanged(nameof(HasAssets));
            OnSelectedAssetChanged();
        }
    }

    private void AssetNamesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasAssets));
        OnSelectedAssetChanged();
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
            OnPropertyChanged(nameof(ReleaseStatus));
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
            OnPropertyChanged(nameof(CurrentVersion));
            OnPropertyChanged(nameof(IsUpToDate));
            OnPropertyChanged(nameof(CanUninstall));
            OnPropertyChanged(nameof(NeedsDebianPackageName));
        }
    }
    
    private string _lastDownloadTag = string.Empty;
    public string LastDownloadTag
    {
        get => _lastDownloadTag;
        set
        {
            if (_lastDownloadTag == value) return;
            _lastDownloadTag = value;
            OnPropertyChanged(nameof(LastDownloadTag));
            OnPropertyChanged(nameof(CurrentVersion));
            OnPropertyChanged(nameof(IsUpToDate));
        }
    }

    private bool _includePrereleases;
    public bool IncludePrereleases
    {
        get => _includePrereleases;
        set
        {
            if (_includePrereleases == value) return;
            _includePrereleases = value;
            if (!_deserializing && TargetTag == "latest") ClearOfferedRelease();
            OnPropertyChanged(nameof(IncludePrereleases));
            OnPropertyChanged(nameof(ReleaseChannel));
            OnPropertyChanged(nameof(ReleaseStatus));
        }
    }

    [JsonIgnore]
    public string ReleaseChannel => IncludePrereleases ? "Stable releases and pre-releases" : "Stable releases only";

    [JsonIgnore]
    public string ReleaseStatus => HasRelease ? $"Offered version: {Tag}"
        : TargetTag != "latest" ? $"No published release found for tag '{TargetTag}'."
        : $"No published releases available in channel: {ReleaseChannel}. Check for updates to refresh.";

    private void ClearOfferedRelease()
    {
        HasRelease = false;
        ReplaceAssets([], []);
        LatestChangelog = "";
        ReleaseDate = "";
        Tag = "";
    }

    private string _targetTag = "latest";
    public string TargetTag
    {
        get => _targetTag;
        set
        {
            if (_targetTag == value || value == null) return;
            _targetTag = value;
            if (!_deserializing) ClearOfferedRelease();
            OnPropertyChanged(nameof(ReleaseStatus));
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
    public bool IsUpToDate => Tag == CurrentVersion;

    [JsonIgnore]
    public string CurrentVersion => IsDownloadOnly && LastDownloadTag.Length > 0 ? LastDownloadTag : CurrentInstallTag;

    [JsonIgnore]
    public bool IsDownloadOnly => !(AutoExtract && ArchiveExtractor.IsSupportedArchive(SelectedAssetName))
        && !UpdateManager.IsInstallerAsset(SelectedAssetName);

    private string _installedDebianPackage = string.Empty;
    public string InstalledDebianPackage
    {
        get => _installedDebianPackage;
        set
        {
            value ??= string.Empty;
            if (_installedDebianPackage == value) return;
            _installedDebianPackage = value;
            OnPropertyChanged(nameof(InstalledDebianPackage));
            OnPropertyChanged(nameof(CanUninstall));
            OnPropertyChanged(nameof(NeedsDebianPackageName));
        }
    }

    private string _installedAppImageId = string.Empty;
    public string InstalledAppImageId
    {
        get => _installedAppImageId;
        set
        {
            value ??= string.Empty;
            if (_installedAppImageId == value) return;
            _installedAppImageId = value;
            OnPropertyChanged(nameof(InstalledAppImageId));
            OnPropertyChanged(nameof(CanUninstall));
            OnPropertyChanged(nameof(NeedsDebianPackageName));
        }
    }

    [JsonIgnore]
    public bool CanUninstall => System.OperatingSystem.IsLinux()
        && (InstalledDebianPackage.Length > 0 || InstalledAppImageId.Length > 0
            || CurrentInstallTag.Length > 0 && Path.GetExtension(SelectedAssetName).ToLowerInvariant() is ".deb" or ".appimage");

    [JsonIgnore]
    public bool NeedsDebianPackageName => InstalledDebianPackage.Length == 0 && InstalledAppImageId.Length == 0
        && CurrentInstallTag.Length > 0 && Path.GetExtension(SelectedAssetName).Equals(".deb", System.StringComparison.OrdinalIgnoreCase);

    private void OnSelectedAssetChanged()
    {
        OnPropertyChanged(nameof(DownloadAssetIndex));
        OnPropertyChanged(nameof(SelectedAssetName));
        OnPropertyChanged(nameof(SelectedAssetUrl));
        OnPropertyChanged(nameof(IsDownloadOnly));
        OnPropertyChanged(nameof(CurrentVersion));
        OnPropertyChanged(nameof(IsUpToDate));
        OnPropertyChanged(nameof(HasAssetRule));
        OnPropertyChanged(nameof(AssetRuleMatches));
        OnPropertyChanged(nameof(HasMultipleAssetRuleMatches));
        OnPropertyChanged(nameof(HasNoAssetRuleMatches));
        OnPropertyChanged(nameof(AssetSelectionStatus));
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(NeedsDebianPackageName));
    }

    private bool _hasRelease = true;
    public bool HasRelease
    {
        get => _hasRelease;
        set
        {
            if (_hasRelease == value) return;
            _hasRelease = value;
            OnPropertyChanged(nameof(HasRelease));
            OnPropertyChanged(nameof(ReleaseStatus));
        }
    }

    [JsonIgnore]
    public bool HasAssets => AssetNames.Count > 0;

    [JsonIgnore]
    public string SelectedAssetName =>
        DownloadAssetIndex >= 0 && DownloadAssetIndex < AssetNames.Count
            ? AssetNames[DownloadAssetIndex]
            : string.Empty;

    [JsonIgnore]
    public string SelectedAssetUrl =>
        DownloadAssetIndex >= 0 && DownloadAssetIndex < DownloadUrls.Count
            ? DownloadUrls[DownloadAssetIndex]
            : string.Empty;
    
    private string _downloadPath = DirectoryHelper.GetUserDirPath();
    public string DownloadPath
    {
        get => _downloadPath;
        set
        {
            if (_downloadPath == value) return;
            _downloadPath = value;
            OnPropertyChanged(nameof(DownloadPath));
            OnPropertyChanged(nameof(ExtractionDirectory));
        }
    }

    private bool _autoExtract;
    public bool AutoExtract
    {
        get => _autoExtract;
        set
        {
            if (_autoExtract == value) return;
            _autoExtract = value;
            OnPropertyChanged(nameof(AutoExtract));
            OnPropertyChanged(nameof(IsDownloadOnly));
            OnPropertyChanged(nameof(CurrentVersion));
            OnPropertyChanged(nameof(IsUpToDate));
        }
    }

    [JsonIgnore]
    public string ExtractionDirectory =>
        Path.Combine(DownloadPath, string.IsNullOrEmpty(Name) ? "extracted" : Name.Replace('/', '-'));

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
        return $"{Name} - " + (IsUpToDate ? CurrentVersion : "\x1b[38;2;255;165;0m" + CurrentVersion + " -> " + Tag + "\x1b[0m");
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
