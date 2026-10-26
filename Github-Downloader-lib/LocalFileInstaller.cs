using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Github_Downloader_lib.Models;

namespace Github_Downloader_lib;

public static class LocalFileInstaller
{
    public static IReadOnlyList<string> SupportedExtensions { get; } = Array.AsReadOnly(
        new[] { ".exe", ".msi" }.Concat(OperatingSystem.IsLinux() ? new[] { ".appimage" } : [])
            .Concat(InstallationManager.CanInstallDebianPackages ? new[] { ".deb" } : [])
            .Concat(InstallationManager.CanInstallArchPackages ? new[] { ".pkg.tar.zst", ".pacman" } : [])
            .Concat(ArchiveExtractor.SupportedExtensions).ToArray());

    public static Task<bool> InstallAsync(string path, Action<string> status, Action<string> progress,
        string? extractionDirectory = null) =>
        InstallAsync(path, status, progress, extractionDirectory, null, FileManager.CachePath, LocalInstallerRetention.LoadLimit());

    public static Task<bool> InstallAsync(string path, Action<string> status, Action<string> progress,
        string? extractionDirectory, Func<string, IReadOnlyList<string>, Task<string?>> selectExecutable) =>
        InstallAsync(path, status, progress, extractionDirectory, null, FileManager.CachePath,
            LocalInstallerRetention.LoadLimit(), selectExecutable);

    internal static async Task<bool> InstallAsync(string path, Action<string> status, Action<string> progress,
        string? extractionDirectory, Func<UpdateManager.Asset, Task<bool>>? installer, string cachePath, int? versionsToKeep = LocalInstallerRetention.DefaultVersionsToKeep,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectExecutable = null, ArchiveAppLauncher? launcher = null,
        string? existingAppId = null)
    {
        if (versionsToKeep < 0) throw new ArgumentOutOfRangeException(nameof(versionsToKeep));
        string source = Path.GetFullPath(path);
        if (!File.Exists(source)) throw new FileNotFoundException("The selected local file does not exist.");
        bool archive = ArchiveExtractor.IsSupportedArchive(source);
        if (!archive && !UpdateManager.IsInstallerAsset(source))
            throw new ArgumentException("Choose a supported installer or archive for this operating system.");
        string filename = Path.GetFileName(source);
        // A separate namespace and filename hash avoid collisions with tracked repositories.
        string name = Regex.Replace(Path.GetFileNameWithoutExtension(filename), "[^A-Za-z0-9_.-]", "-");
        name = name[..Math.Min(64, name.Length)];
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(filename)))[..12].ToLowerInvariant();
        if (archive && string.IsNullOrWhiteSpace(extractionDirectory))
        {
            if (!OperatingSystem.IsLinux()) throw new ArgumentException("Choose an extraction folder for this archive.");
            string managedPath = Path.Combine(LocalApps.Store.AppsPath, $"{name}-{hash}");
            extractionDirectory = managedPath;
            IReadOnlyList<LocalApp> apps = LocalApps.Store.Read();
            // Reuse tracked installations, but never merge into an unrelated directory.
            for (int copy = 2; File.Exists(extractionDirectory) || Directory.Exists(extractionDirectory)
                && !apps.Any(app => app.Kind == LocalAppKind.Archive && app.ExtractionDirectory == extractionDirectory); copy++)
                extractionDirectory = managedPath + "-" + copy;
        }
        string? destination = archive ? Path.GetFullPath(extractionDirectory!) : null;
        Repo local = new() { Url = "", Name = $"local/{name}-{hash}", AutoExtract = archive };
        LocalAppKind kind = archive ? LocalAppKind.Archive
            : InstallationManager.IsArchPackageFile(source) ? LocalAppKind.Arch
            : Path.GetExtension(source).Equals(".deb", StringComparison.OrdinalIgnoreCase) ? LocalAppKind.Debian
            : Path.GetExtension(source).Equals(".appimage", StringComparison.OrdinalIgnoreCase) ? LocalAppKind.AppImage
            : LocalAppKind.Executable;
        // An extraction folder represents one local application, even if its archive is renamed for a new version.
        string folderIdentity = destination is null ? "" : Path.TrimEndingDirectorySeparator(destination);
        if (OperatingSystem.IsWindows()) folderIdentity = folderIdentity.ToUpperInvariant();
        string identity = archive ? "archive:" + folderIdentity : local.Name;
        // A restored app keeps its catalog identity even when its installation folder changes.
        string id = existingAppId ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        LocalApp? previous = LocalApps.Store.Read().FirstOrDefault(app => app.Id == id);
        if (existingAppId is not null && previous is null)
            throw new IOException("The local app entry no longer exists.");
        List<string> extracted = [];
        string temporary = Path.Combine(Path.GetFullPath(cachePath), "local-" + Guid.NewGuid().ToString("N"));
        UpdateManager.Asset asset = new(local, Path.Combine(temporary, filename), "",
            TemporaryDirectory: temporary, LocalSourcePath: source, ExtractionDirectory: destination,
            ExtractionFileWritten: archive ? (file, _) => extracted.Add(file) : null);
        // Keep the original outside the disposable cache, including for installers that move their input.
        // Reinstalling an already saved file can reuse that copy.
        LocalAppStore store = LocalApps.Store;
        bool keepInstaller = versionsToKeep != 0;
        bool reuse = store.Read().Any(app => app.InstallerPath == source
            || app.InstallerVersions.Any(version => version.Path == source));
        string folderName = Path.GetFileNameWithoutExtension(filename);
        if (folderName is "" or "." or "..") folderName = filename + "-installer";
        string savedDirectory = Path.Combine(store.InstallersPath, folderName);
        for (int copy = 2; Directory.Exists(savedDirectory) || File.Exists(savedDirectory); copy++)
            savedDirectory = Path.Combine(store.InstallersPath, $"{folderName}-{copy}");
        string savedPath = reuse ? source : Path.Combine(savedDirectory, filename);
        bool saved = false;
        try
        {
            if (!reuse && keepInstaller)
            {
                Directory.CreateDirectory(savedDirectory);
                await Task.Run(() => File.Copy(source, savedPath));
            }
            bool success = await UpdateManager.ApplyAssetAsync(asset, status, progress, false, installer);
            if (!success) return false;
            // Package identity stays stable when a Debian installer filename changes between versions.
            if (existingAppId is null && kind == LocalAppKind.Debian && local.InstalledDebianPackage.Length > 0) id = "deb:" + local.InstalledDebianPackage;
            if (existingAppId is null && kind == LocalAppKind.Arch && local.InstalledArchPackage.Length > 0) id = "arch:" + local.InstalledArchPackage;
            LocalApp installedApp = new()
            {
                Id = id, FileName = filename, InstallationName = local.Name, Kind = kind,
                InstalledAt = DateTimeOffset.UtcNow, DebianPackage = local.InstalledDebianPackage, ArchPackage = local.InstalledArchPackage,
                AppImageId = local.InstalledAppImageId, ExtractionDirectory = destination ?? "",
                InstallerPath = keepInstaller ? savedPath : ""
            };
            if (archive)
                installedApp = installedApp with
                {
                    ExecutablePath = previous?.ExecutablePath ?? "", DesktopEntryHash = previous?.DesktopEntryHash ?? "",
                    DesktopIconPath = previous?.DesktopIconPath ?? ""
                };
            // Keep extracted files tracked even if selecting or creating a launcher fails.
            store.SaveInstallation(installedApp, versionsToKeep);
            saved = true;
            if (archive)
            {
                LocalApp stored = store.Read().Single(app => app.Id == id);
                LocalApp configured = await (launcher ?? new ArchiveAppLauncher()).ConfigureAsync(
                    stored, previous, extracted, selectExecutable, progress);
                store.Save(configured);
            }
            return true;
        }
        finally
        {
            if (!reuse && !saved && Directory.Exists(savedDirectory)) Directory.Delete(savedDirectory, recursive: true);
        }
    }
}
