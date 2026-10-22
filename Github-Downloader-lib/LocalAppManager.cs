using System.Security.Cryptography;
using Github_Downloader_lib.Models;

namespace Github_Downloader_lib;

public sealed class LocalAppManager
{
    private readonly LocalAppStore _store;
    private readonly InstallationManager _installations;

    public LocalAppManager() : this(LocalApps.Store, new()) { }
    internal LocalAppManager(LocalAppStore store, InstallationManager installations)
    {
        _store = store;
        _installations = installations;
    }

    public Task<bool> ReinstallAsync(LocalApp app, Action<string> status, Action<string> progress) =>
        LocalFileInstaller.InstallAsync(app.InstallerPath, status, progress,
            app.Kind == LocalAppKind.Archive ? app.ExtractionDirectory : null);

    public string GetUninstallDescription(LocalApp app) => app.Kind switch
    {
        LocalAppKind.Archive => "Removes extracted app files. Changed files and your original archive are kept.",
        LocalAppKind.AppImage => "Removes the AppImage and its application-menu entry. Your original file is kept.",
        LocalAppKind.Debian => app.DebianPackage.Length > 0 ? $"Removes the Debian package '{app.DebianPackage}'." : "Removes the Debian package named below.",
        _ => "Use the application's system uninstaller."
    };

    public async Task<bool> UninstallAsync(LocalApp app, Action<string> status, Action<string> progress,
        string? packageName = null)
    {
        if (!app.CanUninstall) throw new InvalidOperationException("Use the application's system uninstaller.");
        if (app.Kind != LocalAppKind.Archive)
        {
            bool removed = await _installations.UninstallAsync(app.ToRepo(), status, progress, packageName, app.FileName);
            if (removed) _store.Remove(app.Id);
            return removed;
        }
        using HistoryScope history = OperationHistory.Store.Begin(HistoryAction.Uninstall, localFileName: app.FileName);
        try
        {
            if (!Path.IsPathFullyQualified(app.ExtractionDirectory)) throw new IOException("The extraction directory is invalid.");
            string root = Path.GetFullPath(app.ExtractionDirectory);
            ArchiveExtractor.EnsureNoLinks(root);
            List<(string Path, string Hash)> files = new();
            foreach (KeyValuePair<string, string> file in app.OwnedFiles)
            {
                string path = ArchiveExtractor.GetEntryPath(root, file.Key);
                ArchiveExtractor.EnsureNoLinks(path);
                if (file.Value.Length != 64 || file.Value.Any(character => !Uri.IsHexDigit(character)))
                    throw new IOException("The installed file record is invalid.");
                files.Add((path, file.Value));
            }
            status($"Uninstalling {app.Name}...");
            bool changed = false;
            foreach ((string path, string hash) in files)
            {
                // Recheck links immediately before deletion and retain user edits.
                ArchiveExtractor.EnsureNoLinks(path);
                if (!File.Exists(path)) continue;
                if (FileHash(path) != hash) { changed = true; continue; }
                File.Delete(path);
            }
            _store.Remove(app.Id);
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
