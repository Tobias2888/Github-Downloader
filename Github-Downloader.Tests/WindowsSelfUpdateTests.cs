using System.ComponentModel;
using System.Diagnostics;
using Github_Downloader.Enums;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class WindowsSelfUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghd-self-update-test-" + Guid.NewGuid().ToString("N"));

    public WindowsSelfUpdateTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void SelfUpdateRunsLastWithoutReorderingOtherRepositoriesOrDownloads()
    {
        Repo self = OwnRepository();
        Repo first = new() { Name = "owner/first", Url = "https://api.github.com/repos/owner/first/releases/latest" };
        Repo second = new() { Name = "owner/second", Url = "https://api.github.com/repos/owner/second/releases/latest" };
        Repo[] repositories = [first, self, second];

        Assert.Equal(new[] { first, second, self }, WindowsSelfUpdate.OrderRepositories(repositories,
            downloadOnly: false, WindowsSelfUpdate.IsOwnInstaller));
        Assert.Equal(repositories, WindowsSelfUpdate.OrderRepositories(repositories,
            downloadOnly: true, WindowsSelfUpdate.IsOwnInstaller));
        Assert.Equal(new[] { first, self, second }, repositories);
    }

    [Fact]
    public void HandoffOnlyAppliesToTheWindowsDesktopAndItsOwnSetupAsset()
    {
        Repo repo = OwnRepository();
        string executable = Path.Combine(_root, "Github-Downloader.exe");
        Assert.True(WindowsSelfUpdate.ShouldHandle(repo, true, Platform.Avalonia, executable));
        Assert.False(WindowsSelfUpdate.ShouldHandle(repo, false, Platform.Avalonia, executable));
        Assert.False(WindowsSelfUpdate.ShouldHandle(repo, true, Platform.Terminal, executable));
        Assert.False(WindowsSelfUpdate.ShouldHandle(repo, true, Platform.Avalonia, Path.Combine(_root, "ghd.exe")));
        repo.Name = "someone-else/Github-Downloader";
        Assert.False(WindowsSelfUpdate.ShouldHandle(repo, true, Platform.Avalonia, executable));
        repo.Name = "Tobias2888/Github-Downloader";
        repo.Url = "https://example.com/repos/Tobias2888/Github-Downloader/releases/latest";
        Assert.False(WindowsSelfUpdate.ShouldHandle(repo, true, Platform.Avalonia, executable));
        repo = OwnRepository("github-downloader.zip");
        Assert.False(WindowsSelfUpdate.ShouldHandle(repo, true, Platform.Avalonia, executable));
    }

    [Fact]
    public void InstallerSurvivesCacheCleanupAndReceivesTheProcessAndInstallationDirectory()
    {
        string cache = Path.Combine(_root, "cache");
        Directory.CreateDirectory(cache);
        string downloaded = Path.Combine(cache, "Github-Downloader-Setup-x64-4.0.0.exe");
        File.WriteAllText(downloaded, "installer payload");
        string executable = Path.Combine(_root, "Program Files", "Github-Downloader", "Github-Downloader.exe");
        WindowsSelfUpdate updater = new();
        ProcessStartInfo? started = null;

        updater.Launch(downloaded, executable, 12345, Path.Combine(_root, "staging"), info =>
        {
            Assert.False(updater.ShutdownRequested);
            started = info;
        });

        Assert.True(updater.ShutdownRequested);
        Assert.NotNull(started);
        Assert.True(started.UseShellExecute);
        Assert.Equal($"/UPDATEPID=12345 /D={Path.GetDirectoryName(executable)}", started.Arguments);
        Directory.Delete(cache, recursive: true);
        Assert.Equal("installer payload", File.ReadAllText(started.FileName));
    }

    [Fact]
    public void CancelledElevationKeepsAppRunningAndAllowsRetry()
    {
        string downloaded = Path.Combine(_root, "setup.exe");
        File.WriteAllText(downloaded, "installer payload");
        string staging = Path.Combine(_root, "staging");
        string executable = Path.Combine(_root, "Github-Downloader.exe");
        WindowsSelfUpdate updater = new();

        Assert.Throws<Win32Exception>(() => updater.Launch(downloaded, executable, 12345,
            staging, _ => throw new Win32Exception(1223)));
        Assert.False(updater.ShutdownRequested);
        Assert.Empty(Directory.GetDirectories(staging));
        Assert.True(File.Exists(downloaded));
        updater.Launch(downloaded, executable, 12345, staging, _ => { });
        Assert.True(updater.ShutdownRequested);
    }

    [Theory]
    [InlineData("v4.0.0", "4.0.0", "v4.0.0")]
    [InlineData("v4.0.0", "3.2.0", "v3.2.0")]
    [InlineData("v2.0.0", "4.0.0", "4.0.0")]
    [InlineData("v4.0.0-beta.1", "4.0.0-beta.1", "v4.0.0-beta.1")]
    public void StartupConfirmsTheRunningVersionRatherThanAssumingSetupSucceeded(
        string offeredVersion, string runningVersion, string expected)
    {
        Repo repo = OwnRepository();
        repo.CurrentInstallTag = "v3.2.0";
        repo.Tag = offeredVersion;
        Repo other = new() { Name = "owner/other", Url = "https://api.github.com/repos/owner/other/releases/latest", CurrentInstallTag = "v1" };

        WindowsSelfUpdate.ReconcileInstalledVersions([repo, other], runningVersion);

        Assert.Equal(expected, repo.CurrentInstallTag);
        Assert.Equal("v1", other.CurrentInstallTag);
    }

    [Fact]
    public void RefreshAfterStartupRecognizesTheInstalledTagWithItsReleasePrefix()
    {
        Repo repo = OwnRepository();
        repo.Tag = "v3.2.0";
        WindowsSelfUpdate.ReconcileInstalledVersions([repo], "4.0.0");
        Assert.Equal("4.0.0", repo.CurrentInstallTag);

        repo.Tag = "v4.0.0";
        WindowsSelfUpdate.ReconcileInstalledVersions([repo], "4.0.0");

        Assert.Equal("v4.0.0", repo.CurrentInstallTag);
        Assert.True(repo.IsUpToDate);
    }

    private static Repo OwnRepository(string asset = "Github-Downloader-Setup-x64-4.0.0.exe") => new()
    {
        Name = "Tobias2888/Github-Downloader",
        Url = "https://api.github.com/repos/Tobias2888/Github-Downloader/releases/latest",
        AssetNames = [asset],
        DownloadUrls = ["https://github.com/Tobias2888/Github-Downloader/releases/download/v4.0.0/" + asset],
        DownloadAssetIndex = 0,
        Tag = "v4.0.0"
    };

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
