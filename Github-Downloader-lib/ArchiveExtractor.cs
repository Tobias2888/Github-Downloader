using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using Github_Downloader_lib.Models;

namespace Github_Downloader_lib;

public static class ArchiveExtractor
{
    private static readonly string[] Extensions =
    [
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz", ".tbz2",
        ".xz", ".txz", ".zst", ".tzst", ".lz"
    ];

    public static bool IsSupportedArchive(string fileName) =>
        Extensions.Any(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public static Task ExtractAsync(string archivePath, string destination, Action<string>? progress = null) =>
        Task.Run(() => Extract(archivePath, destination, progress));

    internal static async Task ExtractDownloadAsync(Repo repo, string archivePath, string tag, Action<string> progress)
    {
        try
        {
            await ExtractAsync(archivePath, repo.ExtractionDirectory, progress);
        }
        finally
        {
            File.Delete(archivePath);
        }
        repo.CurrentInstallTag = tag;
    }

    private static void Extract(string archivePath, string destination, Action<string>? progress)
    {
        destination = Path.GetFullPath(destination);
        EnsureNoLinks(destination);
        string parent = Path.GetDirectoryName(destination)
            ?? throw new IOException("Choose an extraction folder below the filesystem root.");
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, $".github-downloader-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);

        try
        {
            // 7z needs the seekable archive API; the reader API unwraps compressed TARs.
            using IArchive? archive = archivePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
                ? ArchiveFactory.OpenArchive(archivePath)
                : null;
            using IReader reader = archive?.ExtractAllEntries() ?? ReaderFactory.OpenReader(archivePath);
            ExtractionOptions options = new()
            {
                Overwrite = true,
                PreserveFileTime = true
            };

            while (reader.MoveToNextEntry())
            {
                IEntry entry = reader.Entry;
                if (entry.IsEncrypted)
                    throw new IOException("Auto-extract does not support password-protected archives.");
                if (!string.IsNullOrEmpty(entry.LinkTarget))
                    throw new IOException($"Archive links are not supported: {entry.Key}");

                string key = string.IsNullOrEmpty(entry.Key)
                    ? Path.GetFileNameWithoutExtension(archivePath)
                    : entry.Key;
                string output = GetEntryPath(staging, key);
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(output);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                progress?.Invoke($"Extracting {key}");
                reader.WriteEntryToFile(output, options);
                if (!OperatingSystem.IsWindows() && entry is SharpCompress.Common.Tar.TarEntry tarEntry)
                    File.SetUnixFileMode(output, (UnixFileMode)(tarEntry.Mode & 0x1FF));
            }

            // Validate all destinations before merging. Keep unrelated files, such as user settings.
            string[] directories = Directory.GetDirectories(staging, "*", SearchOption.AllDirectories);
            string[] files = Directory.GetFiles(staging, "*", SearchOption.AllDirectories);
            foreach (string directory in directories)
            {
                string target = GetEntryPath(destination, Path.GetRelativePath(staging, directory));
                EnsureNoLinks(target);
                if (File.Exists(target)) throw new IOException($"A file blocks the extraction folder: {target}");
            }
            foreach (string file in files)
            {
                string target = GetEntryPath(destination, Path.GetRelativePath(staging, file));
                EnsureNoLinks(target);
                if (Directory.Exists(target)) throw new IOException($"A folder blocks the extracted file: {target}");
            }

            Directory.CreateDirectory(destination);
            foreach (string directory in directories)
                Directory.CreateDirectory(GetEntryPath(destination, Path.GetRelativePath(staging, directory)));
            foreach (string file in files)
                File.Move(file, GetEntryPath(destination, Path.GetRelativePath(staging, file)), overwrite: true);
        }
        finally
        {
            foreach (string file in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(staging, recursive: true);
        }
    }

    private static string GetEntryPath(string root, string key)
    {
        string relative = key.Replace('\\', '/');
        if (relative.StartsWith('/') || relative.Contains(':') || relative.Split('/').Contains(".."))
            throw new IOException($"Archive entry points outside the extraction folder: {key}");

        string result = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!result.StartsWith(prefix, comparison) && !result.Equals(root, comparison))
            throw new IOException($"Archive entry points outside the extraction folder: {key}");
        return result;
    }

    private static void EnsureNoLinks(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Extraction paths cannot contain symbolic links: {current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
