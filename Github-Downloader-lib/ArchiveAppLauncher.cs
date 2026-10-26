using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Github_Downloader_lib.Models;
using LoggerLib;

namespace Github_Downloader_lib;

internal sealed class ArchiveAppLauncher
{
    private readonly string _desktopDirectory;

    internal ArchiveAppLauncher(string? desktopDirectory = null)
    {
        string? dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        _desktopDirectory = desktopDirectory ?? Path.Combine(
            !string.IsNullOrEmpty(dataHome) && Path.IsPathFullyQualified(dataHome) ? dataHome
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "applications");
    }

    internal string EntryPath(LocalApp app) => Path.Combine(_desktopDirectory,
        "github-downloader-archive-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(app.Id))).ToLowerInvariant() + ".desktop");

    internal async Task<LocalApp> ConfigureAsync(LocalApp app, LocalApp? previous, IReadOnlyList<string> extracted,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectExecutable, Action<string> progress)
    {
        if (!OperatingSystem.IsLinux()) return app;
        string? executable = previous is not null && extracted.Contains(previous.ExecutablePath)
            && File.Exists(previous.ExecutablePath) ? previous.ExecutablePath : null;
        if (executable is null)
        {
            string[] candidates = extracted.Where(IsExecutable).Order(StringComparer.Ordinal).ToArray();
            executable = candidates.Length == 1 ? candidates[0]
                : selectExecutable is null ? null : await selectExecutable(app.ExtractionDirectory, candidates);
        }
        if (string.IsNullOrWhiteSpace(executable))
        {
            if (previous is not null) Remove(previous);
            progress("Archive extracted. No executable selected; no application-menu entry was created.");
            return app with { ExecutablePath = "", DesktopEntryHash = "" };
        }

        executable = Path.GetFullPath(executable);
        string relative = Path.GetRelativePath(app.ExtractionDirectory, executable);
        if (!Path.IsPathFullyQualified(app.ExtractionDirectory)
            || ArchiveExtractor.GetEntryPath(app.ExtractionDirectory, relative) != executable || !File.Exists(executable))
            throw new IOException("Choose an executable inside the extracted application folder.");
        ArchiveExtractor.EnsureNoLinks(executable);
        // ZIP files often lose executable permission. This only makes the selected file runnable.
        File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
        string icon = extracted.Where(path => Path.GetFileName(path).Equals("icon.png", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Equals("icon.svg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Length).FirstOrDefault() ?? "application-x-executable";
        string content = $"""
            [Desktop Entry]
            Type=Application
            Name={EscapeValue(app.Name)}
            Exec={QuoteExecutable(executable)}
            Path={EscapeValue(Path.GetDirectoryName(executable)!)}
            Icon={EscapeValue(icon)}
            Terminal=false
            Categories=Utility;
            X-Github-Downloader-Archive={EscapeValue(app.Id)}
            """ + "\n";
        string entry = EntryPath(app);
        ArchiveExtractor.EnsureNoLinks(entry);
        if (File.Exists(entry) && (previous is null || previous.DesktopEntryHash.Length == 0
            || LocalAppManager.FileHash(entry) != previous.DesktopEntryHash))
            throw new IOException("The application-menu entry was changed or belongs to another application.");
        Directory.CreateDirectory(_desktopDirectory);
        await File.WriteAllTextAsync(entry, content);
        Logger.LogI("Local archive application-menu entry created");
        progress("Added the extracted application to the application menu.");
        return app with { ExecutablePath = executable, DesktopEntryHash = LocalAppManager.FileHash(entry) };
    }

    internal void Remove(LocalApp app)
    {
        if (!OperatingSystem.IsLinux() || app.DesktopEntryHash.Length == 0) return;
        string entry = EntryPath(app);
        ArchiveExtractor.EnsureNoLinks(entry);
        if (!File.Exists(entry)) return;
        if (LocalAppManager.FileHash(entry) != app.DesktopEntryHash)
        {
            Logger.LogW("Changed local archive application-menu entry was kept");
            return;
        }
        File.Delete(entry);
        Logger.LogI("Local archive application-menu entry removed");
    }

    internal static string EscapeValue(string value) => value.Replace("\\", "\\\\")
        .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    internal static string QuoteExecutable(string path)
    {
        if (path.Contains('=')) throw new IOException("Desktop launchers cannot use an executable path containing '='.");
        string quoted = path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$");
        return "\"" + EscapeValue(quoted).Replace("%", "%%") + "\"";
    }

    internal static bool IsExecutable(string path)
    {
        string filename = Path.GetFileName(path);
        if (filename.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
            || filename.Contains(".so.", StringComparison.OrdinalIgnoreCase)) return false;
        using FileStream file = File.OpenRead(path);
        byte[] header = new byte[64];
        int length = file.Read(header, 0, header.Length);
        if (length >= 2 && header[0] == '#' && header[1] == '!') return true;
        if (length < 52 || header[0] != 0x7f || header[1] != 'E' || header[2] != 'L' || header[3] != 'F') return false;
        bool little = header[5] == 1;
        ushort Read16(int offset) => little ? BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(offset))
            : BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(offset));
        ulong Read64(int offset) => little ? BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(offset))
            : BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(offset));
        uint Read32(int offset) => little ? BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset))
            : BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(offset));
        if (Read16(16) == 2) return true; // ET_EXEC
        if (Read16(16) != 3) return false;
        bool is64 = header[4] == 2;
        if (is64 && length < 64) return false;
        ulong programOffset = is64 ? Read64(32) : Read32(28);
        ushort entrySize = Read16(is64 ? 54 : 42);
        ushort count = Read16(is64 ? 56 : 44);
        if (entrySize < 4 || programOffset > (ulong)file.Length
            || (ulong)entrySize * count > (ulong)file.Length - programOffset) return false;
        for (int index = 0; index < count; index++)
        {
            file.Position = (long)programOffset + index * entrySize;
            if (file.Read(header, 0, 4) != 4) return false;
            if (Read32(0) == 3) return true; // PT_INTERP distinguishes PIE applications from shared libraries.
        }
        return false;
    }
}
