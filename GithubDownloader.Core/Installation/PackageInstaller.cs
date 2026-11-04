using GithubDownloader.Core.Operations;
using GithubDownloader.Core.Installation.Launchers;
using GithubDownloader.Core.Configuration;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FileLib;
using GithubDownloader.Core.Enums;
using LoggerLib;

namespace GithubDownloader.Core.Installation;

using Asset = GithubDownloader.Core.Operations.RepositoryAsset;

/// <summary>Runs native installers using authorization captured at the start of an operation.</summary>
internal sealed class PackageInstaller(Platform platform, string? rootPassword)
{
    internal static bool IsInstallerAsset(string name) =>
        IsInstallerAsset(name, OperatingSystem.IsLinux(), InstallationManager.CanInstallDebianPackages, InstallationManager.CanInstallArchPackages);

    internal static bool IsInstallerAsset(string name, bool isLinux, bool canInstallDebianPackages, bool canInstallArchPackages = false)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();
        return isLinux && (extension == ".appimage" || extension == ".deb" && canInstallDebianPackages
                || InstallationManager.IsArchPackageFile(name) && canInstallArchPackages)
            || extension is ".exe" or ".msi";
    }

    private readonly Platform _platform = platform;
    private readonly string? _rootPassword = rootPassword;
    internal async Task<bool> InstallAssetAsync(Asset asset, Action<string> progressText)
    {
        if (InstallationManager.CanInstallArchPackages && InstallationManager.IsArchPackageFile(asset.TempAssetPath))
            return await InstallArchAssetAsync(asset, progressText);
        if (OperatingSystem.IsLinux() && asset.TempAssetPath.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
        {
            InstallationManager installations = new();
            string package = await installations.ReadDebianPackageNameAsync(asset.TempAssetPath);
            if (!await InstallDebsAsync([asset.TempAssetPath], progressText)) return false;
            asset.Repo.InstalledDebianPackage = package;
            return true;
        }
        if (OperatingSystem.IsLinux() && asset.TempAssetPath.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase))
        {
            await HandleAppImagesAsync([asset]);
            asset.Repo.InstalledAppImageId = InstallationManager.GetAppImageId(asset.Repo);
            return true;
        }
        return await InstallExeAsync([asset.TempAssetPath], progressText);
    }

    private async Task HandleAppImagesAsync(List<Asset> assets)
    {
        foreach (Asset asset in assets)
        {
            _ = Logger.LogI("Installing AppImage");

            string appImageId = InstallationManager.GetAppImageId(asset.Repo);
            string assetPath = Path.Join(FileManager.AppImagesPath, appImageId);
            DirectoryHelper.CreateDir(assetPath);
            string destPath = Path.Join(assetPath, appImageId + ".AppImage");
            string iconPath = Path.Join(assetPath, "icon.png");
            File.Move(asset.TempAssetPath, destPath, overwrite: true);

            await RunAppImageCommandAsync(new ProcessStartInfo
            {
                FileName = "chmod", ArgumentList = { "+x", destPath },
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            }, "AppImage permission update");
            await RunAppImageCommandAsync(new ProcessStartInfo
            {
                FileName = destPath, ArgumentList = { "--appimage-extract" },
                WorkingDirectory = FileManager.CachePath,
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            }, "AppImage resource extraction");

            string tempIconPath = Path.Combine(FileManager.CachePath, "squashfs-root", ".DirIcon");
            do
            {
                FileInfo fileInfo = new(tempIconPath);
                if (fileInfo.LinkTarget != null)
                {
                    tempIconPath = Path.Join(FileManager.CachePath, "squashfs-root", fileInfo.LinkTarget);
                }
            } while (new FileInfo(tempIconPath).LinkTarget != null);

            File.Move(tempIconPath, iconPath, overwrite: true);

            CreateStartMenuEntry(asset with { TempAssetPath = destPath }, iconPath);
            _ = Logger.LogI("AppImage installed");
        }
    }

    private async Task RunAppImageCommandAsync(ProcessStartInfo start, string action)
    {
        _ = Logger.LogI($"{action} started");
        using Process process = new() { StartInfo = start };
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        // Drain both streams without logging paths or file names printed by AppImage tools.
        await Task.WhenAll(output, errors);
        if (process.ExitCode != 0)
        {
            _ = Logger.LogE($"{action} failed (exit code {process.ExitCode})");
            throw new InvalidOperationException($"{action} failed (exit code {process.ExitCode}).");
        }
        _ = Logger.LogI($"{action} completed");
    }

    private void CreateStartMenuEntry(Asset asset, string iconPath)
    {
        string displayName = asset.LocalSourcePath is null ? asset.Repo.Name
            : Path.GetFileNameWithoutExtension(asset.LocalSourcePath);
        displayName = displayName.Replace('\r', ' ').Replace('\n', ' ').Replace("\\", "\\\\");
        string desktopFile = $"""
                             [Desktop Entry]
                             Name={displayName}
                             Comment={asset.Repo.Description}
                             GenericName={displayName}
                             Exec={DesktopEntryCommand.Create(asset.TempAssetPath, asset.Repo.DesktopEntryArguments)}
                             Icon={iconPath}
                             Type=Application
                             StartupNotify=false
                             Categories=Utility;
                             """;

        string desktopDirectoryPath = Path.Join(DirectoryHelper.GetUserDirPath(), ".local", "share", "applications");
        string desktopFilePath = Path.Join(desktopDirectoryPath, InstallationManager.GetAppImageId(asset.Repo) + ".desktop");
        DirectoryHelper.CreateDir(desktopDirectoryPath);
        FileHelper.Create(desktopFilePath);
        File.WriteAllText(desktopFilePath, desktopFile);
    }

    internal async Task<bool> InstallDebsAsync(List<string> debPaths, Action<string> progressText)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return true;
        }

        string[] packages = debPaths.Where(path => Path.GetExtension(path).Equals(".deb", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath).ToArray();
        if (packages.Length == 0) return true;

        bool usePkexec = _platform == Platform.Avalonia;
        bool providePassword = !usePkexec && !string.IsNullOrEmpty(_rootPassword);

        _ = Logger.LogI($"Installing Debian packages (count {debPaths.Count})");
        progressText("Installing Debian packages...");

        ProcessStartInfo start = CreatePackageInstallStartInfo(InstallationManager.DebianInstallerPath);
        foreach (string argument in new[] { "install", "-y", "--allow-downgrades", "--reinstall", "--" })
            start.ArgumentList.Add(argument);
        foreach (string package in packages) start.ArgumentList.Add(package);
        int exitCode = await RunPackageInstallCommandAsync(start, progressText);

        if (exitCode == 0)
        {
            _ = Logger.LogI("Installation complete");
            return true;
        }

        string hint = providePassword && exitCode == 1
            ? " (the supplied root password may be incorrect)"
            : "";
        _ = Logger.LogE($"Installation failed with exit code {exitCode}");
        progressText($"Installation failed with exit code {exitCode}{hint}");
        return false;
    }

    internal ProcessStartInfo CreatePackageInstallStartInfo(string packageManager)
    {
        bool usePkexec = _platform == Platform.Avalonia;
        bool providePassword = !usePkexec && !string.IsNullOrEmpty(_rootPassword);
        ProcessStartInfo start = new()
        {
            FileName = "/usr/bin/" + (usePkexec ? "pkexec" : "sudo"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = providePassword,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (providePassword)
        {
            start.ArgumentList.Add("-S");
            start.ArgumentList.Add("-p");
            start.ArgumentList.Add("");
        }
        start.ArgumentList.Add(packageManager);
        return start;
    }

    internal async Task<bool> InstallArchAssetAsync(Asset asset, Action<string> progressText,
        InstallationManager? installations = null, Func<ProcessStartInfo, Action<string>, Task<int>>? run = null)
    {
        installations ??= new();
        string package = await installations.ReadArchPackageNameAsync(asset.TempAssetPath);
        if (!await InstallArchPackageAsync(asset.TempAssetPath, progressText, run)) return false;
        asset.Repo.InstalledArchPackage = package;
        return true;
    }

    internal async Task<bool> InstallArchPackageAsync(string path, Action<string> progressText,
        Func<ProcessStartInfo, Action<string>, Task<int>>? run = null)
    {
        _ = Logger.LogI("Installing Arch package");
        progressText("Installing Arch package...");
        ProcessStartInfo start = CreatePackageInstallStartInfo(InstallationManager.ArchInstallerPath);
        foreach (string argument in new[] { "-U", "--noconfirm", "--", Path.GetFullPath(path) })
            start.ArgumentList.Add(argument);
        int exitCode = await (run ?? RunPackageInstallCommandAsync)(start, progressText);
        if (exitCode == 0)
        {
            _ = Logger.LogI("Installation complete");
            return true;
        }
        _ = Logger.LogE($"Installation failed with exit code {exitCode}");
        progressText($"Installation failed with exit code {exitCode}");
        return false;
    }

    internal async Task<int> RunPackageInstallCommandAsync(ProcessStartInfo start, Action<string> progressText)
    {
        using Process process = new() { StartInfo = start };
        process.Start();
        Task output = ForwardInstallationOutputAsync(process.StandardOutput, progressText, isStandardError: false);
        Task errors = ForwardInstallationOutputAsync(process.StandardError, progressText, isStandardError: true);
        if (start.RedirectStandardInput)
        {
            await process.StandardInput.WriteLineAsync(_rootPassword);
            process.StandardInput.Close();
        }
        await process.WaitForExitAsync();
        await Task.WhenAll(output, errors);
        if (process.ExitCode != 0)
            await Logger.LogE($"Package manager exited with code {process.ExitCode}");
        return process.ExitCode;
    }

    // stderr is also used for warnings and status messages; the exit code determines failure.
    internal static LogLevel ClassifyOutput(string line, bool isStandardError)
    {
        string message = line.TrimStart();
        foreach (string prefix in new[] { "error:", "fehler:", "fatal:", "E:" })
            if (message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return LogLevel.Error;
        foreach (string prefix in new[] { "warning:", "warnung:", "W:" })
            if (message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return LogLevel.Warning;
        return isStandardError ? LogLevel.Warning : LogLevel.Info;
    }

    private static async Task ForwardInstallationOutputAsync(StreamReader reader, Action<string> progressText, bool isStandardError)
    {
        while (await reader.ReadLineAsync() is string line)
        {
            LogLevel level = ClassifyOutput(line, isStandardError);
            string message = $"Package manager: {line}";
            if (level == LogLevel.Error) await Logger.LogE(message);
            else if (level == LogLevel.Warning) await Logger.LogW(message);
            else await Logger.LogI(message);
            progressText(line);
        }
    }

    private async Task<bool> InstallExeAsync(List<string> exePaths, Action<string> progressText)
    {
        bool allOk = true;
        foreach (string exePath in exePaths)
        {
            _ = Logger.LogI("Starting executable installer");

            using Process process = new()
            {
                StartInfo = new()
                    {
                    FileName = exePath,
                    UseShellExecute = true // Important to open GUI installer
                }
            };

            process.Start();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _ = Logger.LogI("Installation complete");
            }
            else
            {
                _ = Logger.LogE($"Installation failed with exit code {process.ExitCode}");
                allOk = false;
            }
        }

        return allOk;
    }
}
