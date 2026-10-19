using System.Diagnostics;
using System.Text.RegularExpressions;
using FileLib;
using LoggerLib;
using Github_Downloader.Enums;
using Github_Downloader_lib.Models;

namespace Github_Downloader_lib;

public sealed class InstallationManager
{
    private readonly string _appImagesDirectory;
    private readonly string _desktopDirectory;
    private readonly Func<ProcessStartInfo, Action<string>, Task<CommandResult>> _run;

    public InstallationManager() : this(FileManager.AppImagesPath,
        Path.Combine(DirectoryHelper.GetUserDirPath(), ".local", "share", "applications"), RunCommandAsync) { }

    internal InstallationManager(string appImagesDirectory, string desktopDirectory,
        Func<ProcessStartInfo, Action<string>, Task<CommandResult>> run)
    {
        _appImagesDirectory = appImagesDirectory;
        _desktopDirectory = desktopDirectory;
        _run = run;
    }

    internal readonly record struct CommandResult(int ExitCode, string Output);

    public static bool IsValidDebianPackageName(string? name) => name is not null
        && Regex.IsMatch(name, @"\A[a-z0-9][a-z0-9+.-]+(?::[a-z0-9][a-z0-9-]*)?\z");

    public static string GetAppImageId(Repo repo)
    {
        string id = repo.Name.Replace('/', '-');
        ValidateAppImageId(id);
        return id;
    }

    private static void ValidateAppImageId(string id)
    {
        if (id is "." or ".." || !Regex.IsMatch(id, @"\A[A-Za-z0-9_.-]+\z"))
            throw new InvalidOperationException("The AppImage installation name is invalid.");
    }

    public async Task<string> ReadDebianPackageNameAsync(string debPath)
    {
        Logger.LogI("Debian package metadata read started");
        try
        {
            ProcessStartInfo start = CreateStartInfo("/usr/bin/dpkg-deb");
            start.ArgumentList.Add("--field");
            start.ArgumentList.Add(Path.GetFullPath(debPath));
            start.ArgumentList.Add("Package");
            start.ArgumentList.Add("Architecture");
            CommandResult result = await _run(start, _ => { });
            if (result.ExitCode != 0) throw new InvalidOperationException("Could not read the Debian package metadata.");

            string package = string.Empty;
            string architecture = string.Empty;
            foreach (string line in result.Output.Split('\n', StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith("Package: ", StringComparison.Ordinal)) package = line[9..].Trim();
                if (line.StartsWith("Architecture: ", StringComparison.Ordinal)) architecture = line[14..].Trim();
            }
            if (!Regex.IsMatch(architecture, @"\A[a-z0-9][a-z0-9-]*\z"))
                throw new InvalidOperationException("The Debian package architecture is invalid.");
            string name = architecture == "all" ? package : $"{package}:{architecture}";
            if (!IsValidDebianPackageName(name)) throw new InvalidOperationException("The Debian package name is invalid.");
            Logger.LogI("Debian package metadata read completed");
            return name;
        }
        catch (Exception exception)
        {
            Logger.LogE($"Debian package metadata read failed ({exception.GetType().Name})");
            throw;
        }
    }

    public async Task<bool> UninstallAsync(Repo repo, Action<string> status, Action<string> progress,
        string? legacyDebianPackage = null, string? localFileName = null)
    {
        Logger.LogI("Repository uninstall started");
        using HistoryScope history = OperationHistory.Store.Begin(HistoryAction.Uninstall, repo, localFileName: localFileName);
        try
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Uninstall is supported on Linux.");
            if (!repo.CanUninstall) throw new InvalidOperationException("This repository has no supported installation to uninstall.");

            string package = repo.InstalledDebianPackage;
            if (repo.NeedsDebianPackageName) package = legacyDebianPackage?.Trim() ?? string.Empty;
            if ((repo.NeedsDebianPackageName || package.Length > 0) && !IsValidDebianPackageName(package))
                throw new InvalidOperationException("Enter the installed Debian package name.");

            string appImageId = repo.InstalledAppImageId;
            if (appImageId.Length == 0 && repo.InstalledDebianPackage.Length == 0
                && Path.GetExtension(repo.SelectedAssetName).Equals(".appimage", StringComparison.OrdinalIgnoreCase))
                appImageId = GetAppImageId(repo);
            if (appImageId.Length > 0) ValidateAppImageInstallation(appImageId);

            status($"Uninstalling {repo.Name}...");
            if (package.Length > 0)
            {
                Logger.LogI("Removing Debian package");
                progress("Removing Debian package");
                ProcessStartInfo start = CreateDebianRemovalStartInfo(package);
                // Command output can contain user paths; do not put it in activity logs.
                CommandResult result = await _run(start, _ => { });
                if (result.ExitCode != 0)
                {
                    Logger.LogE($"Debian package removal failed (exit code {result.ExitCode})");
                    progress($"Uninstall failed (exit code {result.ExitCode}). Check authorization and package dependencies.");
                    history.Finish(HistoryStatus.Failed, HistoryOutcome.UninstallFailed);
                    return false;
                }
                Logger.LogI("Debian package removed");
                repo.InstalledDebianPackage = string.Empty;
            }

            if (appImageId.Length > 0)
            {
                Logger.LogI("Removing AppImage installation");
                progress("Removing the AppImage and its application-menu entry");
                RemoveAppImage(appImageId);
                Logger.LogI("AppImage installation removed");
                repo.InstalledAppImageId = string.Empty;
            }

            repo.CurrentInstallTag = string.Empty;
            repo.ExcludedFromDownloadAll = true;
            status($"Uninstalled {repo.Name}");
            Logger.LogI("Repository uninstall completed");
            history.Complete(HistoryOutcome.Uninstalled);
            return true;
        }
        catch (Exception exception)
        {
            Logger.LogE($"Repository uninstall failed ({exception.GetType().Name})");
            history.Finish(HistoryStatus.Failed, HistoryOutcome.UninstallFailed);
            throw;
        }
    }

    public string GetUninstallDescription(Repo repo, string? legacyDebianPackage = null)
    {
        if (!repo.CanUninstall)
            return "This installation must be removed manually.";

        List<string> actions = [];
        if (repo.InstalledDebianPackage.Length > 0 || repo.NeedsDebianPackageName)
        {
            string package = repo.NeedsDebianPackageName
                ? legacyDebianPackage?.Trim() ?? string.Empty : repo.InstalledDebianPackage;
            string target = package.Length > 0 ? $"'{package}'" : "named below";
            actions.Add($"Removes the Debian package {target}.");
        }

        bool hasAppImage = repo.InstalledAppImageId.Length > 0
            || repo.InstalledDebianPackage.Length == 0
            && Path.GetExtension(repo.SelectedAssetName).Equals(".appimage", StringComparison.OrdinalIgnoreCase);
        if (hasAppImage)
            actions.Add("Removes the AppImage and its application-menu entry.");

        actions.Add("Your settings and saved downloads are kept.");
        return string.Join(" ", actions);
    }

    internal static ProcessStartInfo CreateDebianRemovalStartInfo(string package)
    {
        if (!IsValidDebianPackageName(package)) throw new ArgumentException("Invalid Debian package name.", nameof(package));
        bool desktop = UpdateManager.CurPlatform == Platform.Avalonia;
        ProcessStartInfo start = CreateStartInfo(desktop ? "/usr/bin/pkexec" : "/usr/bin/sudo");
        if (!desktop && !string.IsNullOrEmpty(UpdateManager.RootPassword))
        {
            start.ArgumentList.Add("-S");
            start.ArgumentList.Add("-p");
            start.ArgumentList.Add(string.Empty);
            start.RedirectStandardInput = true;
        }
        // dpkg removes this exact package and refuses to break dependency checks.
        // Unlike apt-get, it does not interpret package names as patterns or actions.
        start.ArgumentList.Add("/usr/bin/dpkg");
        start.ArgumentList.Add("--remove");
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(package);
        return start;
    }

    private void ValidateAppImageInstallation(string id)
    {
        ValidateAppImageId(id);
        string directory = Path.Combine(_appImagesDirectory, id);
        if (new DirectoryInfo(directory).LinkTarget is not null)
            throw new InvalidOperationException("The AppImage directory is a symbolic link and cannot be uninstalled automatically.");
        string desktopFile = Path.Combine(_desktopDirectory, id + ".desktop");
        if (File.Exists(desktopFile))
        {
            string expected = "Exec=" + Path.Combine(directory, id + ".AppImage");
            if (!File.ReadLines(desktopFile).Contains(expected))
                throw new InvalidOperationException("The application-menu entry belongs to another application.");
        }
    }

    private void RemoveAppImage(string id)
    {
        string directory = Path.Combine(_appImagesDirectory, id);
        File.Delete(Path.Combine(_desktopDirectory, id + ".desktop"));
        if (!Directory.Exists(directory)) return;
        File.Delete(Path.Combine(directory, id + ".AppImage"));
        File.Delete(Path.Combine(directory, "icon.png"));
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    private static ProcessStartInfo CreateStartInfo(string fileName) => new(fileName)
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true
    };

    private static async Task<CommandResult> RunCommandAsync(ProcessStartInfo start, Action<string> progress)
    {
        using Process process = new() { StartInfo = start };
        process.Start();
        Task<string> output = ReadOutputAsync(process.StandardOutput, progress);
        Task<string> errors = ReadOutputAsync(process.StandardError, progress);
        if (start.RedirectStandardInput)
        {
            await process.StandardInput.WriteLineAsync(UpdateManager.RootPassword);
            process.StandardInput.Close();
        }
        await process.WaitForExitAsync();
        string response = await output;
        await errors;
        return new(process.ExitCode, response);
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, Action<string> progress)
    {
        System.Text.StringBuilder output = new();
        while (await reader.ReadLineAsync() is string line)
        {
            output.AppendLine(line);
            progress(line);
        }
        return output.ToString();
    }
}
