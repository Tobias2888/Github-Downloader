using System.Diagnostics;
using Github_Downloader.Enums;
using Github_Downloader_lib.Models;

namespace Github_Downloader_lib;

public sealed class WindowsSelfUpdate
{
    private static readonly string StagingRoot = Path.Combine(FileManager.AppdataPath, "self-update");

    public bool ShutdownRequested { get; private set; }

    internal static bool IsSelfUpdate(Repo repo) => ShouldHandle(repo, OperatingSystem.IsWindows(),
        UpdateManager.CurPlatform, Environment.ProcessPath);

    internal static bool ShouldHandle(Repo repo, bool windows, Platform platform, string? processPath)
        => windows && platform == Platform.Avalonia
            && string.Equals(Path.GetFileName(processPath), "Github-Downloader.exe", StringComparison.OrdinalIgnoreCase)
            && IsOwnInstaller(repo);

    internal static bool IsOwnInstaller(Repo repo)
        => repo.Name.Equals("Tobias2888/Github-Downloader", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(repo.Url, UriKind.Absolute, out Uri? url)
            && url.Scheme == Uri.UriSchemeHttps
            && url.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            && url.AbsolutePath.TrimEnd('/').Equals("/repos/Tobias2888/Github-Downloader/releases/latest",
                StringComparison.OrdinalIgnoreCase)
            && repo.SelectedAssetName.StartsWith("Github-Downloader-Setup-", StringComparison.OrdinalIgnoreCase)
            && repo.SelectedAssetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    internal static Repo[] OrderRepositories(IEnumerable<Repo> repositories, bool downloadOnly,
        Func<Repo, bool>? isSelfUpdate = null)
        => downloadOnly ? repositories.ToArray()
            : repositories.OrderBy(isSelfUpdate ?? IsSelfUpdate).ToArray();

    internal void Launch(string installerPath, string processPath, int processId,
        string stagingRoot, Action<ProcessStartInfo> start)
    {
        if (ShutdownRequested) throw new InvalidOperationException("A self-update is already in progress.");
        string directory = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string stagedInstaller = Path.Combine(directory, Path.GetFileName(installerPath));
            File.Copy(installerPath, stagedInstaller);
            ProcessStartInfo info = new()
            {
                FileName = stagedInstaller,
                UseShellExecute = true,
                // NSIS requires /D to be last and its path to be unquoted, even with spaces.
                Arguments = $"/UPDATEPID={processId} /D={Path.GetDirectoryName(processPath)}"
            };
            start(info);
            ShutdownRequested = true;
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    internal void Launch(string installerPath)
    {
        // Save the completed repositories before handing control to the installer.
        FileManager.SaveRepos();
        Launch(installerPath, Environment.ProcessPath!, Environment.ProcessId, StagingRoot, info =>
        {
            using Process? installer = Process.Start(info);
            if (installer is null) throw new IOException("The self-update installer could not be started.");
        });
    }

    public static void Initialize(IEnumerable<Repo> repositories)
    {
        if (!OperatingSystem.IsWindows() || UpdateManager.CurPlatform != Platform.Avalonia
            || !string.Equals(Path.GetFileName(Environment.ProcessPath), "Github-Downloader.exe", StringComparison.OrdinalIgnoreCase))
            return;

        ReconcileInstalledVersions(repositories, AppInfo.Version);
        if (!Directory.Exists(StagingRoot)) return;
        foreach (string directory in Directory.GetDirectories(StagingRoot))
        {
            // An installer may still be open. Leave its files until a later startup.
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void ReconcileInstalledVersions(IEnumerable<Repo> repositories, string installedVersion)
    {
        foreach (Repo repo in repositories.Where(IsOwnInstaller))
        {
            string normalized = installedVersion.TrimStart('v', 'V');
            if (repo.Tag.TrimStart('v', 'V').Equals(normalized, StringComparison.OrdinalIgnoreCase))
                repo.CurrentInstallTag = repo.Tag;
            else if (!repo.CurrentInstallTag.TrimStart('v', 'V').Equals(normalized, StringComparison.OrdinalIgnoreCase))
                repo.CurrentInstallTag = installedVersion;
        }
    }
}
