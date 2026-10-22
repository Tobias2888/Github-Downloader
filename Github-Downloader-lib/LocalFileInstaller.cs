using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Github_Downloader_lib.Models;

namespace Github_Downloader_lib;

public static class LocalFileInstaller
{
    public static IReadOnlyList<string> SupportedExtensions { get; } = Array.AsReadOnly(
        new[] { ".exe", ".msi" }.Concat(OperatingSystem.IsLinux() ? new[] { ".deb", ".appimage" } : [])
            .Concat(ArchiveExtractor.SupportedExtensions).ToArray());

    public static Task<bool> InstallAsync(string path, Action<string> status, Action<string> progress,
        string? extractionDirectory = null) =>
        InstallAsync(path, status, progress, extractionDirectory, null, FileManager.CachePath);

    internal static async Task<bool> InstallAsync(string path, Action<string> status, Action<string> progress,
        string? extractionDirectory, Func<UpdateManager.Asset, Task<bool>>? installer, string cachePath)
    {
        string source = Path.GetFullPath(path);
        if (!File.Exists(source)) throw new FileNotFoundException("The selected local file does not exist.");
        bool archive = ArchiveExtractor.IsSupportedArchive(source);
        if (!archive && !UpdateManager.IsInstallerAsset(source))
            throw new ArgumentException("Choose a supported installer or archive for this operating system.");
        if (archive && string.IsNullOrWhiteSpace(extractionDirectory))
            throw new ArgumentException("Choose an extraction folder for this archive.");
        string? destination = archive ? Path.GetFullPath(extractionDirectory!) : null;
        string filename = Path.GetFileName(source);
        // A separate namespace and filename hash avoid collisions with tracked repositories.
        string name = Regex.Replace(Path.GetFileNameWithoutExtension(filename), "[^A-Za-z0-9_.-]", "-");
        name = name[..Math.Min(64, name.Length)];
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(filename)))[..12].ToLowerInvariant();
        Repo local = new() { Url = "", Name = $"local/{name}-{hash}", AutoExtract = archive };
        LocalAppKind kind = archive ? LocalAppKind.Archive
            : Path.GetExtension(source).Equals(".deb", StringComparison.OrdinalIgnoreCase) ? LocalAppKind.Debian
            : Path.GetExtension(source).Equals(".appimage", StringComparison.OrdinalIgnoreCase) ? LocalAppKind.AppImage
            : LocalAppKind.Executable;
        // An extraction folder represents one local application, even if its archive is renamed for a new version.
        string folderIdentity = destination is null ? "" : Path.TrimEndingDirectorySeparator(destination);
        if (OperatingSystem.IsWindows()) folderIdentity = folderIdentity.ToUpperInvariant();
        string identity = archive ? "archive:" + folderIdentity : local.Name;
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        LocalApp? previous = LocalApps.Store.Read().FirstOrDefault(app => app.Id == id);
        Dictionary<string, string> owned = previous is null ? new() : new(previous.OwnedFiles);
        void FileWritten(string file, bool existed)
        {
            string relative = Path.GetRelativePath(destination!, file);
            if (!existed || owned.ContainsKey(relative)) owned[relative] = LocalAppManager.FileHash(file);
        }
        string temporary = Path.Combine(Path.GetFullPath(cachePath), "local-" + Guid.NewGuid().ToString("N"));
        UpdateManager.Asset asset = new(local, Path.Combine(temporary, filename), "",
            TemporaryDirectory: temporary, LocalSourcePath: source, ExtractionDirectory: destination,
            ExtractionFileWritten: archive ? FileWritten : null);
        // Keep the original outside the disposable cache, including for installers that move their input.
        // Reinstalling an already saved file can reuse that copy.
        LocalAppStore store = LocalApps.Store;
        bool reuse = store.Read().Any(app => app.InstallerPath == source);
        string folderName = Path.GetFileNameWithoutExtension(filename);
        if (folderName is "" or "." or "..") folderName = filename + "-installer";
        string savedDirectory = Path.Combine(store.InstallersPath, folderName);
        for (int copy = 2; Directory.Exists(savedDirectory) || File.Exists(savedDirectory); copy++)
            savedDirectory = Path.Combine(store.InstallersPath, $"{folderName}-{copy}");
        string savedPath = reuse ? source : Path.Combine(savedDirectory, filename);
        bool saved = false;
        try
        {
            if (!reuse)
            {
                Directory.CreateDirectory(savedDirectory);
                await Task.Run(() => File.Copy(source, savedPath));
            }
            bool success = await UpdateManager.ApplyAssetAsync(asset, status, progress, false, installer);
            if (!success) return false;
            // Package identity stays stable when a Debian installer filename changes between versions.
            if (kind == LocalAppKind.Debian && local.InstalledDebianPackage.Length > 0) id = "deb:" + local.InstalledDebianPackage;
            store.Save(new()
            {
                Id = id, FileName = filename, InstallationName = local.Name, Kind = kind,
                InstalledAt = DateTimeOffset.UtcNow, DebianPackage = local.InstalledDebianPackage,
                AppImageId = local.InstalledAppImageId, ExtractionDirectory = destination ?? "", OwnedFiles = owned,
                InstallerPath = savedPath
            });
            saved = true;
            return true;
        }
        finally
        {
            if (!reuse && !saved && Directory.Exists(savedDirectory)) Directory.Delete(savedDirectory, recursive: true);
        }
    }
}
