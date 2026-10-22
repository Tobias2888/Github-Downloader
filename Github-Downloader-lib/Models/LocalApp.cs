using System.Text.Json.Serialization;

namespace Github_Downloader_lib.Models;

[JsonConverter(typeof(JsonStringEnumConverter<LocalAppKind>))]
public enum LocalAppKind { Debian, AppImage, Archive, Executable }

public sealed record LocalApp
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string InstallationName { get; init; }
    public LocalAppKind Kind { get; init; }
    public DateTimeOffset InstalledAt { get; init; }
    public string DebianPackage { get; init; } = "";
    public string AppImageId { get; init; } = "";
    public string ExtractionDirectory { get; init; } = "";
    public string InstallerPath { get; init; } = "";
    public Dictionary<string, string> OwnedFiles { get; init; } = new();
    [JsonIgnore] public string Name => Path.GetFileNameWithoutExtension(FileName);
    [JsonIgnore] public bool CanReinstall => File.Exists(InstallerPath);
    [JsonIgnore] public string Type => Kind switch
    {
        LocalAppKind.Debian => "Debian package",
        LocalAppKind.AppImage => "AppImage",
        LocalAppKind.Archive => "Extracted archive",
        _ => "Executable installer"
    };
    [JsonIgnore] public bool CanUninstall => Kind == LocalAppKind.Archive
        || OperatingSystem.IsLinux() && Kind is LocalAppKind.Debian or LocalAppKind.AppImage;
    [JsonIgnore] public bool UseSystemUninstaller => Kind == LocalAppKind.Executable && OperatingSystem.IsWindows();
    [JsonIgnore] public bool NeedsManualRemoval => !CanUninstall;

    public Repo ToRepo() => new()
    {
        Url = "", Name = InstallationName, CurrentInstallTag = "local",
        InstalledDebianPackage = DebianPackage, InstalledAppImageId = AppImageId,
        AssetNames = [FileName], AssetName = FileName
    };
}
