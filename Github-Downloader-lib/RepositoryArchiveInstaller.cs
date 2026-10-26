using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Github_Downloader_lib.Models;

namespace Github_Downloader_lib;

internal static class RepositoryArchiveInstaller
{
    internal static string GetInstallationDirectory(Repo repo)
    {
        if (!Regex.IsMatch(repo.Name, @"\A[A-Za-z0-9-]+/[A-Za-z0-9._-]+\z"))
            throw new IOException("The repository installation name is invalid.");
        string[] parts = repo.Name.ToLowerInvariant().Split('/');
        if (parts[1] is "." or "..") throw new IOException("The repository installation name is invalid.");
        return Path.Combine(LocalApps.Store.AppsPath, parts[0], parts[1]);
    }

    internal static string LegacyInstallationDirectory(Repo repo)
    {
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(repo.Name))).ToLowerInvariant();
        return Path.Combine(LocalApps.Store.AppsPath, "repository-" + identity);
    }

    private static LocalApp? MigrateInstallation(Repo repo, string destination)
    {
        LocalApp? previous = repo.ArchiveInstallation;
        string legacy = LegacyInstallationDirectory(repo);
        if (previous?.ExtractionDirectory != legacy || !Directory.Exists(legacy)) return previous;
        ArchiveExtractor.EnsureNoLinks(legacy);
        ArchiveExtractor.EnsureNoLinks(destination);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("The new repository installation folder already exists. Its files were preserved.");
        string executable = previous.ExecutablePath.Length == 0 ? ""
            : ArchiveExtractor.GetEntryPath(destination, Path.GetRelativePath(legacy, previous.ExecutablePath));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(legacy, destination);
        previous = previous with { ExtractionDirectory = destination, ExecutablePath = executable };
        repo.ArchiveInstallation = previous;
        return previous;
    }

    internal static async Task InstallAsync(Repo repo, string archivePath, string tag, Action<string> progress,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectExecutable = null, ArchiveAppLauncher? launcher = null)
    {
        try
        {
            string destination = GetInstallationDirectory(repo);
            LocalApp? previous = MigrateInstallation(repo, destination);
            Dictionary<string, string> owned = previous?.ExtractionDirectory == destination ? new(previous.OwnedFiles) : new();
            List<string> extracted = [];
            void FileWritten(string path, bool existed)
            {
                extracted.Add(path);
                string relative = Path.GetRelativePath(destination, path);
                if (!existed || owned.ContainsKey(relative)) owned[relative] = LocalAppManager.FileHash(path);
            }
            await ArchiveExtractor.ExtractAsync(archivePath, destination, progress, fileWritten: FileWritten);
            LocalApp installed = new()
            {
                Id = previous?.Id ?? "repository:" + repo.Name,
                FileName = Path.GetFileName(archivePath), InstallationName = repo.Name, Kind = LocalAppKind.Archive,
                InstalledAt = DateTimeOffset.UtcNow, ExtractionDirectory = destination, OwnedFiles = owned
            };
            // Keep ownership of extracted files if selecting or creating the launcher fails.
            repo.ArchiveInstallation = installed;
            repo.ArchiveInstallation = await (launcher ?? new ArchiveAppLauncher()).ConfigureAsync(
                installed, previous, extracted, selectExecutable, progress, repo.Name.Split('/').Last());
            repo.CurrentInstallTag = tag;
        }
        finally { File.Delete(archivePath); }
    }
}
