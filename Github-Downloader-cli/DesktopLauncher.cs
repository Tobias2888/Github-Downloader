using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace Github_Downloader_cli;

internal static class DesktopLauncher
{
    internal static int Launch() => Launch(GetExecutablePaths(), Console.Error);

    internal static int Launch(IEnumerable<string> executablePaths, TextWriter error)
    {
        try
        {
            foreach (string executablePath in executablePaths)
            {
                if (!File.Exists(executablePath)) continue;

                using Process? desktop = Process.Start(CreateStartInfo(executablePath));
                if (desktop is not null) return 0;

                error.WriteLine("Could not open GitHub Downloader desktop.");
                return 1;
            }

            error.WriteLine("GitHub Downloader desktop was not found. Install the desktop application or add its executable directory to PATH.");
            return 1;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Could not open GitHub Downloader desktop: {exception.Message}");
            return 1;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executablePath)
    {
        string fullPath = Path.GetFullPath(executablePath);
        ProcessStartInfo start = new(fullPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(fullPath)!
        };
        start.ArgumentList.Add("--gui");
        return start;
    }

    private static IEnumerable<string> GetExecutablePaths()
    {
        string executableName = OperatingSystem.IsWindows() ? "Github-Downloader.exe" : "Github-Downloader";
        yield return Path.Combine(AppContext.BaseDirectory, executableName);

        if (OperatingSystem.IsLinux())
        {
            yield return Path.Combine("/opt", "Github-Downloader", executableName);
        }
        else if (OperatingSystem.IsWindows())
        {
            using RegistryKey? installation = Registry.CurrentUser.OpenSubKey(@"Software\tobias2888\Github-Downloader");
            if (installation?.GetValue("") is string installationDirectory && !string.IsNullOrWhiteSpace(installationDirectory))
                yield return Path.Combine(installationDirectory, executableName);

            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "tobias2888", "Github-Downloader", executableName);
        }

        string searchPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string directory in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory.Trim('"'), executableName);
        }
    }
}
