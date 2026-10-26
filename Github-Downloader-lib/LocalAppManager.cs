using System.Security.Cryptography;
using LoggerLib;
using Github_Downloader_lib.Models;

namespace Github_Downloader_lib;

public sealed class LocalAppManager
{
    private readonly LocalAppStore _store;
    private readonly InstallationManager _installations;
    private readonly ArchiveAppLauncher _launcher;

    public LocalAppManager() : this(LocalApps.Store, new()) { }
    internal LocalAppManager(LocalAppStore store, InstallationManager installations, ArchiveAppLauncher? launcher = null)
    {
        _store = store;
        _installations = installations;
        _launcher = launcher ?? new();
    }

    public Task<bool> ReinstallAsync(LocalApp app, Action<string> status, Action<string> progress,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectExecutable = null) =>
        LocalFileInstaller.InstallAsync(app.InstallerPath, status, progress,
            app.Kind == LocalAppKind.Archive ? app.ExtractionDirectory : null, null, FileManager.CachePath,
            LocalInstallerRetention.LoadLimit(), selectExecutable, _launcher, existingAppId: app.Id);

    public Task<bool> InstallVersionAsync(LocalApp app, LocalInstallerVersion version, Action<string> status, Action<string> progress,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectExecutable = null)
    {
        LocalApp current = _store.Read().FirstOrDefault(entry => entry.Id == app.Id)
            ?? throw new IOException("The local app entry no longer exists.");
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        LocalInstallerVersion selected = current.AvailableInstallerVersions.FirstOrDefault(saved => string.Equals(saved.Path, version.Path, comparison))
            ?? throw new IOException("The selected installer version is no longer available.");
        string root = Path.GetFullPath(_store.InstallersPath) + Path.DirectorySeparatorChar;
        if (!Path.IsPathFullyQualified(selected.Path) || !Path.GetFullPath(selected.Path).StartsWith(root, comparison))
            throw new IOException("The saved installer is outside GHD's installer folder.");
        ArchiveExtractor.EnsureNoLinks(selected.Path);
        return LocalFileInstaller.InstallAsync(selected.Path, status, progress,
            current.Kind == LocalAppKind.Archive ? current.ExtractionDirectory : null, null, FileManager.CachePath,
            LocalInstallerRetention.LoadLimit(), selectExecutable, _launcher, existingAppId: current.Id);
    }

    public Task<bool> InstallNewArchiveVersionAsync(LocalApp app, string path, Action<string> status, Action<string> progress,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectExecutable = null, string? extractionDirectory = null)
    {
        LocalApp current = _store.Read().FirstOrDefault(entry => entry.Id == app.Id)
            ?? throw new IOException("The local app entry no longer exists.");
        if (current.Kind != LocalAppKind.Archive || !ArchiveExtractor.IsSupportedArchive(path))
            throw new ArgumentException("Choose an archive to add a version of this app.");
        string? destination = string.IsNullOrWhiteSpace(current.ExtractionDirectory)
            ? extractionDirectory : current.ExtractionDirectory;
        return LocalFileInstaller.InstallAsync(path, status, progress, destination, null, FileManager.CachePath,
            LocalInstallerRetention.LoadLimit(), selectExecutable, _launcher, existingAppId: current.Id);
    }

    public string GetUninstallDescription(LocalApp app) => (app.Kind switch
    {
        LocalAppKind.Archive => _store.IsManagedAppDirectory(app.ExtractionDirectory)
            ? "Removes the extracted application folder, including files created by the app, and its application-menu entry. Your original archive is kept."
            : "Removes extracted app files and the application-menu entry. Changed files and your original archive are kept.",
        LocalAppKind.AppImage => "Removes the AppImage and its application-menu entry. Your original file is kept.",
        LocalAppKind.Arch => app.ArchPackage.Length > 0 ? $"Removes the Arch package '{app.ArchPackage}'." : "Removes the Arch package named below.",
        LocalAppKind.Debian => app.DebianPackage.Length > 0 ? $"Removes the Debian package '{app.DebianPackage}'." : "Removes the Debian package named below.",
        _ => "Use the application's system uninstaller."
    }) + " The GHD entry and saved installer are kept for reinstalling.";

    public async Task<bool> UninstallAsync(LocalApp app, Action<string> status, Action<string> progress,
        string? packageName = null)
    {
        if (!app.CanUninstall) throw new InvalidOperationException("Use the application's system uninstaller.");
        if (app.Kind != LocalAppKind.Archive)
        {
            bool removed = await _installations.UninstallAsync(app.ToRepo(), status, progress, packageName, app.FileName);
            if (removed) _store.Save(app with { IsInstalled = false });
            return removed;
        }
        return await UninstallArchiveAsync(app, status, progress, _store.Save);
    }

    internal async Task<bool> UninstallArchiveAsync(LocalApp app, Action<string> status, Action<string> progress,
        Action<LocalApp> save, bool removeEntireDirectory = true)
    {
        using HistoryScope history = OperationHistory.Store.Begin(HistoryAction.Uninstall, localFileName: app.FileName);
        try
        {
            if (!Path.IsPathFullyQualified(app.ExtractionDirectory)) throw new IOException("The extraction directory is invalid.");
            string root = Path.GetFullPath(app.ExtractionDirectory);
            ArchiveExtractor.EnsureNoLinks(root);
            status($"Uninstalling {app.Name}...");
            if (removeEntireDirectory && _store.IsManagedAppDirectory(root))
            {
                Logger.LogI("Removing managed local archive application folder");
                progress("Removing extracted application files...");
                // The entire managed folder belongs to this app, including files it creates at runtime.
                // Directory.Delete removes directory links themselves without following their targets.
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                _launcher.Remove(app);
                save(app with { IsInstalled = false });
                history.Complete(HistoryOutcome.Uninstalled);
                status("Uninstalled.");
                return true;
            }
            List<(string Path, string Hash)> files = new();
            foreach (KeyValuePair<string, string> file in app.OwnedFiles)
            {
                string path = ArchiveExtractor.GetEntryPath(root, file.Key);
                ArchiveExtractor.EnsureNoLinks(path);
                if (file.Value.Length != 64 || file.Value.Any(character => !Uri.IsHexDigit(character)))
                    throw new IOException("The installed file record is invalid.");
                files.Add((path, file.Value));
            }
            bool changed = false;
            foreach ((string path, string hash) in files)
            {
                // Recheck links immediately before deletion and retain user edits.
                ArchiveExtractor.EnsureNoLinks(path);
                if (!File.Exists(path)) continue;
                if (FileHash(path) != hash) { changed = true; continue; }
                File.Delete(path);
            }
            // Remove empty app subfolders, while retaining the user-selected extraction root.
            HashSet<string> directories = new();
            foreach ((string path, string _) in files)
                for (string? directory = Path.GetDirectoryName(path); directory is not null && directory != root;
                    directory = Path.GetDirectoryName(directory)) directories.Add(directory);
            foreach (string directory in directories.OrderByDescending(path => path.Length))
            {
                ArchiveExtractor.EnsureNoLinks(directory);
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            _launcher.Remove(app);
            save(app with { IsInstalled = false });
            history.Complete(HistoryOutcome.Uninstalled);
            status(changed ? "Uninstalled. Changed files were kept." : "Uninstalled.");
            return true;
        }
        catch
        {
            history.Finish(HistoryStatus.Failed, HistoryOutcome.UninstallFailed);
            throw;
        }
    }

    internal static string FileHash(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}
