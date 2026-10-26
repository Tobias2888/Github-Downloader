using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using Github_Downloader_lib.Models;

namespace Github_Downloader_lib;

public enum InstallerExportMode { None, Newest, All }

public static class ConfigurationArchive
{
    private static readonly string[] ConfigNames = ["repos.json", "appsettings.json", "local-apps.json"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Export(string destination, InstallerExportMode mode, string dataDirectory)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        string root = Path.GetFullPath(dataDirectory);
        Dictionary<string, string> files = new(StringComparer.Ordinal);
        foreach (string name in ConfigNames)
        {
            string path = Path.Combine(root, name);
            if (File.Exists(path)) files.Add(name, path);
        }
        string installers = Path.Combine(root, "local-installers");
        IEnumerable<string> selected = [];
        if (mode == InstallerExportMode.All && Directory.Exists(installers))
            selected = EnumerateInstallers(installers);
        else if (mode == InstallerExportMode.Newest)
            selected = new LocalAppStore(Path.Combine(root, "local-apps.json")).Read()
                .SelectMany(app => app.AvailableInstallerVersions.Take(1)).Select(version => version.Path);
        foreach (string path in selected)
        {
            string name = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
            ValidateName(name);
            files.TryAdd(name, path);
        }
        foreach (string path in files.Values) ArchiveExtractor.EnsureNoLinks(path);
        string output = Path.GetFullPath(destination);
        if (files.Values.Any(path => Path.GetFullPath(path).Equals(output, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            throw new IOException("The export cannot overwrite a configuration or installer file.");
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                if (IsTarGzip(destination))
                {
                    using GZipStream gzip = new(stream, CompressionLevel.Optimal);
                    using TarWriter writer = new(gzip);
                    foreach (KeyValuePair<string, string> file in files) writer.WriteEntry(file.Value, file.Key);
                }
                else if (destination.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using ZipArchive archive = new(stream, ZipArchiveMode.Create);
                    foreach (KeyValuePair<string, string> file in files)
                        archive.CreateEntryFromFile(file.Value, file.Key, CompressionLevel.Optimal);
                }
                else throw new InvalidDataException("Choose a .zip or .tar.gz archive.");
            }
            File.Move(temporary, output, true);
        }
        finally { File.Delete(temporary); }
    }

    private static IEnumerable<string> EnumerateInstallers(string directory)
    {
        ArchiveExtractor.EnsureNoLinks(directory);
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            ArchiveExtractor.EnsureNoLinks(path);
            if (Directory.Exists(path))
            {
                foreach (string file in EnumerateInstallers(path)) yield return file;
            }
            else yield return path;
        }
    }

    public static bool IsTarGzip(string path) => path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".tag.gz", StringComparison.OrdinalIgnoreCase);

    public static void Import(string source, string dataDirectory, Action<string>? validateSettings = null)
    {
        string staging = Path.Combine(Path.GetTempPath(), "ghd-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            HashSet<string> names = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            void Extract(string name, Stream content)
            {
                ValidateName(name);
                if (!names.Add(name)) throw new InvalidDataException("The archive contains duplicate files.");
                string target = Path.Combine(staging, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write);
                content.CopyTo(output);
            }
            using (FileStream stream = File.OpenRead(source))
            {
                if (IsTarGzip(source))
                {
                    using GZipStream gzip = new(stream, CompressionMode.Decompress);
                    using TarReader reader = new(gzip);
                    TarEntry? entry;
                    while ((entry = reader.GetNextEntry()) is not null)
                    {
                        if (entry.EntryType != TarEntryType.RegularFile && entry.EntryType != TarEntryType.V7RegularFile)
                            throw new InvalidDataException("The archive must contain regular files only.");
                        Extract(entry.Name, entry.DataStream ?? Stream.Null);
                    }
                }
                else
                {
                    using ZipArchive archive = new(stream, ZipArchiveMode.Read);
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                            throw new InvalidDataException("The archive cannot contain symbolic links.");
                        using Stream content = entry.Open();
                        Extract(entry.FullName, content);
                    }
                }
            }
            if (!names.Contains("repos.json") || !names.Contains("appsettings.json"))
                throw new InvalidDataException("The archive must contain repos.json and appsettings.json.");
            List<Repo>? repos = JsonSerializer.Deserialize<List<Repo>>(File.ReadAllText(Path.Combine(staging, "repos.json")));
            if (repos is null || repos.Any(repo => repo is null)) throw new InvalidDataException("Invalid repository configuration.");
            if (repos.Any(repo => repo.ArchiveInstallation is not null))
            {
                foreach (Repo repo in repos.Where(repo => repo.ArchiveInstallation is not null))
                {
                    repo.ArchiveInstallation = null;
                    repo.CurrentInstallTag = "";
                }
                File.WriteAllText(Path.Combine(staging, "repos.json"), JsonSerializer.Serialize(repos, JsonOptions));
            }
            string settings = File.ReadAllText(Path.Combine(staging, "appsettings.json"));
            using (JsonDocument document = JsonDocument.Parse(settings))
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid application settings.");
            validateSettings?.Invoke(settings);
            if (names.Contains("local-apps.json"))
            {
                IReadOnlyList<LocalApp> apps = new LocalAppStore(Path.Combine(staging, "local-apps.json")).Read();
                string Rebase(string path)
                {
                    string normalized = path.Replace('\\', '/');
                    int index = normalized.LastIndexOf("/local-installers/", StringComparison.Ordinal);
                    string relative = index >= 0 ? normalized[(index + 1)..] : normalized;
                    return relative.StartsWith("local-installers/", StringComparison.Ordinal) && names.Contains(relative)
                        ? Path.Combine(Path.GetFullPath(dataDirectory), relative) : "";
                }
                LocalApp[] restored = apps.Select(app => app with
                {
                    // Restoring a catalog does not install its apps on this machine.
                    IsInstalled = false,
                    InstalledAt = default,
                    DebianPackage = "",
                    ArchPackage = "",
                    AppImageId = "",
                    ExtractionDirectory = "",
                    ExecutablePath = "",
                    DesktopEntryHash = "",
                    DesktopIconPath = "",
                    OwnedFiles = null,
                    InstallerPath = Rebase(app.InstallerPath),
                    InstallerVersions = app.InstallerVersions
                        .Concat(app.InstallerPath.Length == 0 ? [] : new[] { new LocalInstallerVersion(app.InstallerPath, app.InstalledAt) })
                        .DistinctBy(version => version.Path).Select(version => version with { Path = Rebase(version.Path) })
                        .Where(version => version.Path.Length > 0).OrderByDescending(version => version.SavedAt).ToList()
                }).Select(app => app with { InstallerPath = app.InstallerPath.Length > 0 ? app.InstallerPath : app.InstallerVersions.FirstOrDefault()?.Path ?? "" }).ToArray();
                File.WriteAllText(Path.Combine(staging, "local-apps.json"), JsonSerializer.Serialize(restored, JsonOptions));
            }
            foreach (string name in names) ArchiveExtractor.EnsureNoLinks(Path.Combine(dataDirectory, name));
            foreach (string name in names.OrderBy(name => ConfigNames.Contains(name)))
            {
                string target = Path.Combine(dataDirectory, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(staging, name), target, true);
            }
        }
        finally { Directory.Delete(staging, true); }
    }

    private static void ValidateName(string name)
    {
        if (ConfigNames.Contains(name)) return;
        string[] parts = name.Split('/');
        if (parts.Length < 2 || parts[0] != "local-installers"
            || parts.Any(part => part.Length == 0 || part is "." or ".." || part.Contains('\\') || part.Contains(':')))
            throw new InvalidDataException("The archive contains an unsupported or unsafe path.");
    }
}
