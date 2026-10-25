using System.Text.Json.Serialization;

namespace Github_Downloader_lib.Models;

[JsonConverter(typeof(JsonStringEnumConverter<LocalAppKind>))]
public enum LocalAppKind { Debian, AppImage, Archive, Executable }

public sealed record LocalInstallerVersion(string Path, DateTimeOffset SavedAt)
{
    [JsonIgnore] public string FileName => System.IO.Path.GetFileName(Path);
    [JsonIgnore] public string SavedAtLabel => $"Saved {SavedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
}

public sealed record LocalApp
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string InstallationName { get; init; }
    public LocalAppKind Kind { get; init; }
    public DateTimeOffset InstalledAt { get; init; }
    public bool IsInstalled { get; init; } = true;
    public string DebianPackage { get; init; } = "";
    public string AppImageId { get; init; } = "";
    public string ExtractionDirectory { get; init; } = "";
    public string InstallerPath { get; init; } = "";
    public List<LocalInstallerVersion> InstallerVersions { get; init; } = new();
    public Dictionary<string, string> OwnedFiles { get; init; } = new();
    [JsonIgnore] public string Name => Path.GetFileNameWithoutExtension(FileName);
    [JsonIgnore] public bool CanReinstall => File.Exists(InstallerPath);
    [JsonIgnore] public IReadOnlyList<LocalInstallerVersion> AvailableInstallerVersions => InstallerVersions
        .Concat(InstallerPath.Length == 0 ? [] : new[] { new LocalInstallerVersion(InstallerPath, InstalledAt) })
        .DistinctBy(version => version.Path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        .Where(version => File.Exists(version.Path)).OrderByDescending(version => version.SavedAt).ToArray();
    [JsonIgnore] public bool CanChooseInstallerVersion => AvailableInstallerVersions.Count > 1;
    [JsonIgnore] public string Type => Kind switch
    {
        LocalAppKind.Debian => "Debian package",
        LocalAppKind.AppImage => "AppImage",
        LocalAppKind.Archive => "Extracted archive",
        _ => "Executable installer"
    };
    [JsonIgnore] public string Status => IsInstalled ? "Installed" : "Uninstalled";
    [JsonIgnore] public bool CanUninstall => IsInstalled && (Kind == LocalAppKind.Archive
        || OperatingSystem.IsLinux() && Kind is LocalAppKind.Debian or LocalAppKind.AppImage);
    [JsonIgnore] public bool UseSystemUninstaller => IsInstalled && Kind == LocalAppKind.Executable && OperatingSystem.IsWindows();

    public Repo ToRepo() => new()
    {
        Url = "", Name = InstallationName, CurrentInstallTag = "local",
        InstalledDebianPackage = DebianPackage, InstalledAppImageId = AppImageId,
        AssetNames = [FileName], AssetName = FileName
    };
}
