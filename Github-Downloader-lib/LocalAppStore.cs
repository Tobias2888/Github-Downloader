using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Github_Downloader_lib.Models;

namespace Github_Downloader_lib;

public static class LocalApps
{
    public static LocalAppStore Store { get; set; } = new(Path.Combine(FileManager.AppdataPath, "local-apps.json"));
}

public sealed class LocalAppStore
{
    private readonly string _path;
    private readonly string _mutexName;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal string InstallersPath => Path.Combine(Path.GetDirectoryName(_path)!, "local-installers");

    public LocalAppStore(string path)
    {
        _path = Path.GetFullPath(path);
        _mutexName = "ghd-local-apps-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_path)));
    }

    public IReadOnlyList<LocalApp> Read() => Locked(apps => apps.OrderByDescending(app => app.InstalledAt).ToArray(), false);

    public void Save(LocalApp app) => Locked(apps =>
    {
        apps.RemoveAll(existing => existing.Id == app.Id);
        apps.Add(app);
        return true;
    }, true);

    public void SaveInstallation(LocalApp app, int? versionsToKeep) => Locked(apps =>
    {
        ValidateLimit(versionsToKeep);
        LocalApp? previous = apps.FirstOrDefault(entry => entry.Id == app.Id);
        List<LocalInstallerVersion> versions = previous is null ? [] : GetVersions(previous);
        foreach (LocalInstallerVersion version in GetVersions(app))
        {
            if (!versions.Any(existing => PathComparer.Equals(existing.Path, version.Path)))
                versions.Add(version);
        }
        LocalApp installed = WithVersions(app, versions);
        apps.RemoveAll(existing => existing.Id == app.Id);
        apps.Add(installed);
        PruneVersions(apps, versionsToKeep, app.Id);
        return true;
    }, true);

    public void ApplyInstallerRetention(int? versionsToKeep) => Locked(apps =>
    {
        ValidateLimit(versionsToKeep);
        PruneVersions(apps, versionsToKeep);
        return true;
    }, true);

    public void Remove(string id) => Locked(apps =>
    {
        LocalApp? app = apps.FirstOrDefault(entry => entry.Id == id);
        if (app is null) return false;
        List<LocalApp> remaining = apps.Where(entry => entry.Id != id).ToList();
        DeleteUnusedInstallers(GetVersions(app), remaining);
        return apps.RemoveAll(entry => entry.Id == id) > 0;
    }, true);

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static void ValidateLimit(int? limit)
    {
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
    }

    private static List<LocalInstallerVersion> GetVersions(LocalApp app)
    {
        // Existing lists recorded just the current installer. Adopt it without changing its age.
        List<LocalInstallerVersion> versions = new(app.InstallerVersions);
        if (app.InstallerPath.Length > 0 && !versions.Any(version => PathComparer.Equals(version.Path, app.InstallerPath)))
            versions.Add(new(app.InstallerPath, app.InstalledAt));
        return versions.DistinctBy(version => version.Path, PathComparer).OrderByDescending(version => version.SavedAt).ToList();
    }

    private static LocalApp WithVersions(LocalApp app, IEnumerable<LocalInstallerVersion> versions)
    {
        List<LocalInstallerVersion> ordered = versions.OrderByDescending(version => version.SavedAt).ToList();
        string path = ordered.Any(version => PathComparer.Equals(version.Path, app.InstallerPath))
            ? app.InstallerPath : ordered.FirstOrDefault()?.Path ?? "";
        return app with { InstallerVersions = ordered, InstallerPath = path };
    }

    private void PruneVersions(List<LocalApp> apps, int? limit, string? onlyApp = null)
    {
        List<LocalApp> retained = new();
        List<LocalInstallerVersion> discarded = new();
        foreach (LocalApp app in apps)
        {
            List<LocalInstallerVersion> versions = GetVersions(app);
            List<LocalInstallerVersion> keep = onlyApp is not null && app.Id != onlyApp
                ? versions : versions.Take(limit ?? int.MaxValue).ToList();
            retained.Add(WithVersions(app, keep));
            discarded.AddRange(versions.Where(version => !keep.Contains(version)));
        }
        DeleteUnusedInstallers(discarded, retained);
        apps.Clear();
        apps.AddRange(retained);
    }

    private void DeleteUnusedInstallers(IEnumerable<LocalInstallerVersion> discarded, IReadOnlyList<LocalApp> remaining)
    {
        HashSet<string> referenced = remaining.SelectMany(GetVersions).Select(version => version.Path).ToHashSet(PathComparer);
        string[] paths = discarded.Select(version => version.Path).Distinct(PathComparer).Where(path => !referenced.Contains(path)).ToArray();
        string root = Path.GetFullPath(InstallersPath);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // Validate the whole deletion set before removing anything, including legacy paths.
        foreach (string path in paths)
        {
            if (!Path.IsPathFullyQualified(path) || !Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, comparison))
                throw new IOException("The saved installer is outside GHD's installer folder.");
            ArchiveExtractor.EnsureNoLinks(path);
        }
        foreach (string path in paths)
        {
            ArchiveExtractor.EnsureNoLinks(path);
            try { File.Delete(path); }
            catch (DirectoryNotFoundException) { }
            string directory = Path.GetDirectoryName(path)!;
            if (!directory.Equals(root, comparison) && Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }

    private T Locked<T>(Func<List<LocalApp>, T> action, bool save)
    {
        using Mutex mutex = new(false, _mutexName);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }
        try
        {
            List<LocalApp> apps;
            try
            {
                apps = File.Exists(_path) ? JsonSerializer.Deserialize<List<LocalApp>>(File.ReadAllText(_path), Options)
                    ?? throw new JsonException() : [];
                if (apps.Any(app => app is null || string.IsNullOrWhiteSpace(app.Id) || string.IsNullOrWhiteSpace(app.FileName)
                    || string.IsNullOrWhiteSpace(app.InstallationName) || !Enum.IsDefined(app.Kind)
                    || app.DebianPackage is null || app.AppImageId is null || app.ExtractionDirectory is null || app.InstallerPath is null || app.OwnedFiles is null || app.InstallerVersions is null
                    || app.InstallerVersions.Any(version => version is null || string.IsNullOrWhiteSpace(version.Path))
                    || app.OwnedFiles.Any(file => string.IsNullOrWhiteSpace(file.Key) || string.IsNullOrWhiteSpace(file.Value))))
                    throw new JsonException();
            }
            catch (JsonException) { throw new InvalidDataException("The local app list could not be read. Its file was preserved."); }
            T result = action(apps);
            if (save)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (FileStream file = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                        JsonSerializer.Serialize(file, apps, Options);
                        file.Flush(true);
                    }
                    File.Move(temporary, _path, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return result;
        }
        finally { mutex.ReleaseMutex(); }
    }
}
