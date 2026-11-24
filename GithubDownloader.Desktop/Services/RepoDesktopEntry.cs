using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GithubDownloader.Core.Installation;
using GithubDownloader.Core.Models;
using FileLib;

namespace GithubDownloader.Desktop.Services;

internal sealed record RepoDesktopEntry(string FilePath, string Executable, IReadOnlyList<string> Arguments, string? WorkingDirectory)
{
    internal static RepoDesktopEntry? Resolve(Repo repo)
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            string? entryPath = FindEntryPath(repo);
            if (entryPath is null || !File.Exists(entryPath)) return null;
            string[] lines = File.ReadAllLines(entryPath);
            if (lines.Any(line => line.Equals("Hidden=true", StringComparison.OrdinalIgnoreCase)
                || line.Equals("NoDisplay=true", StringComparison.OrdinalIgnoreCase)
                || line.Equals("Terminal=true", StringComparison.OrdinalIgnoreCase))) return null;
            string? exec = lines.FirstOrDefault(line => line.StartsWith("Exec=", StringComparison.Ordinal))?[5..];
            if (exec is null || !TryParseExec(exec, out List<string> tokens) || tokens.Count == 0) return null;
            string executable = FindExecutable(tokens[0]);
            if (executable.Length == 0) return null;
            string? workingDirectory = lines.FirstOrDefault(line => line.StartsWith("Path=", StringComparison.Ordinal))?[5..];
            if (workingDirectory is not null) workingDirectory = UnescapeValue(workingDirectory);
            if (workingDirectory is not null && !Directory.Exists(workingDirectory)) workingDirectory = null;
            return new RepoDesktopEntry(entryPath, executable, tokens.Skip(1).ToArray(), workingDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? FindEntryPath(Repo repo)
    {
        string applications = Path.Combine(DataHome(), "applications");
        if (repo.ArchiveInstallation is { IsInstalled: true } archive)
        {
            string id = archive.Id;
            string readable = id.StartsWith("repository:", StringComparison.Ordinal) ? id["repository:".Length..] : "";
            if (readable.Length <= 180 && Regex.IsMatch(readable, @"\A[A-Za-z0-9-]+/[A-Za-z0-9._-]+\z"))
                return Path.Combine(applications, readable.ToLowerInvariant().Replace('/', '_') + ".desktop");
        }

        if (repo.InstalledAppImageId.Length > 0 || repo.InstalledDebianPackage.Length == 0 && repo.InstalledArchPackage.Length == 0
            && repo.SelectedAssetName.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase))
        {
            string id = repo.InstalledAppImageId.Length > 0 ? repo.InstalledAppImageId : InstallationManager.GetAppImageId(repo);
            if (id is "." or ".." || !Regex.IsMatch(id, @"\A[A-Za-z0-9_.-]+\z")) return null;
            string appImageEntry = Path.Combine(DirectoryHelper.GetUserDirPath(), ".local", "share", "applications", id + ".desktop");
            if (File.Exists(appImageEntry)) return appImageEntry;
            string xdgAppImageEntry = Path.Combine(applications, id + ".desktop");
            if (File.Exists(xdgAppImageEntry)) return xdgAppImageEntry;
        }

        string package = repo.InstalledDebianPackage.Length > 0 ? repo.InstalledDebianPackage : repo.InstalledArchPackage;
        if (package.Length == 0) return null;
        IReadOnlyList<string> packageFiles = ListPackageFiles(package, repo.InstalledDebianPackage.Length > 0);
        string[] candidates = packageFiles.Where(path => path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
                && Path.IsPathFullyQualified(path) && File.Exists(path))
            .Distinct(StringComparer.Ordinal).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static IReadOnlyList<string> ListPackageFiles(string package, bool debian)
    {
        string executable = debian ? "dpkg-query" : "pacman";
        ProcessStartInfo start = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (debian)
        {
            start.ArgumentList.Add("--listfiles");
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(package);
        }
        else
        {
            start.ArgumentList.Add("-Ql");
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(package);
        }
        try
        {
            using Process process = new() { StartInfo = start };
            if (!process.Start()) return [];
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0) return [];
            string result = output.GetAwaiter().GetResult();
            _ = error.GetAwaiter().GetResult();
            return result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => debian ? line.TrimEnd('\r') : PacmanPath(line)).Where(path => path.Length > 0).ToArray();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    private static string PacmanPath(string line)
    {
        int pathStart = 0;
        while (pathStart < line.Length && !char.IsWhiteSpace(line[pathStart])) pathStart++;
        while (pathStart < line.Length && char.IsWhiteSpace(line[pathStart])) pathStart++;
        return pathStart < line.Length ? line[pathStart..].TrimEnd('\r') : "";
    }

    private static string DataHome()
    {
        string? configured = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
            ? configured : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
    }

    private static string FindExecutable(string candidate)
    {
        if (Path.IsPathFullyQualified(candidate)) return File.Exists(candidate) ? candidate : "";
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null) return "";
        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string executable = Path.Combine(directory, candidate);
            if (File.Exists(executable)) return executable;
        }
        return "";
    }

    private static bool TryParseExec(string command, out List<string> tokens)
    {
        tokens = [];
        StringBuilder token = new();
        bool inQuotes = false;
        bool escaped = false;
        bool started = false;
        for (int index = 0; index < command.Length; index++)
        {
            char character = command[index];
            if (escaped)
            {
                token.Append(character switch { 's' => ' ', 'n' => '\n', 't' => '\t', 'r' => '\r', _ => character });
                escaped = false;
                started = true;
            }
            else if (character == '\\') { escaped = true; started = true; }
            else if (character == '"') { inQuotes = !inQuotes; started = true; }
            else if (character == '%')
            {
                if (index + 1 >= command.Length) return false;
                char fieldCode = command[index + 1];
                if (fieldCode == '%')
                {
                    token.Append('%');
                    index++;
                    started = true;
                }
                else if (fieldCode is 'f' or 'F' or 'u' or 'U')
                {
                    bool standalone = !started && token.Length == 0
                        && (index + 2 == command.Length || char.IsWhiteSpace(command[index + 2]));
                    if (!standalone) return false;
                    index++;
                }
                else return false;
            }
            else if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (started) { tokens.Add(token.ToString()); token.Clear(); started = false; }
            }
            else { token.Append(character); started = true; }
        }
        if (escaped || inQuotes) return false;
        if (started) tokens.Add(token.ToString());
        return true;
    }

    private static string UnescapeValue(string value) => value.Replace("\\s", " ").Replace("\\n", "\n")
        .Replace("\\t", "\t").Replace("\\r", "\r").Replace("\\\\", "\\");
}
