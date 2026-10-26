using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Github_Downloader.Enums;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class PackageManagerTests
{
    [Theory]
    [InlineData("app.pkg.tar.zst", true, true, true)]
    [InlineData("app.PKG.TAR.ZST", true, true, true)]
    [InlineData("app.pkg.tar.zst", true, false, false)]
    [InlineData("app.pkg.tar.zst", false, true, false)]
    [InlineData("app.tar.zst", true, true, false)]
    [InlineData("app.pkg.tar.zst.sig", true, true, false)]
    [InlineData("app.pacman", true, true, true)]
    [InlineData("app.PACMAN", true, true, true)]
    [InlineData("app.pacman", true, false, false)]
    [InlineData("app.pacman", false, true, false)]
    [InlineData("app.pacman.sig", true, true, false)]
    public void ArchInstallersRequirePacmanAndTheFullPackageSuffix(string name, bool linux, bool pacman, bool expected)
    {
        Assert.Equal(expected, UpdateManager.IsInstallerAsset(name, linux, false, pacman));
    }

    [Theory]
    [InlineData(Platform.Avalonia, 0)]
    [InlineData(Platform.Terminal, 0)]
    [InlineData(Platform.Terminal, 1)]
    [InlineData(Platform.Avalonia, 127)]
    public async Task PacmanUsesAnElevatedUpgradeCommandAndPropagatesFailure(Platform platform, int exitCode)
    {
        Platform previousPlatform = UpdateManager.CurPlatform;
        string? previousPassword = UpdateManager.RootPassword;
        try
        {
            UpdateManager.CurPlatform = platform;
            UpdateManager.RootPassword = "test-password";
            string path = Path.GetFullPath("package with 'quotes'.pkg.tar.zst");
            bool called = false;
            bool result = await UpdateManager.InstallArchPackageAsync(path, _ => { }, (start, _) =>
            {
                called = true;
                Assert.Equal(platform == Platform.Avalonia ? "/usr/bin/pkexec" : "/usr/bin/sudo", start.FileName);
                Assert.Equal(platform == Platform.Terminal, start.RedirectStandardInput);
                string[] arguments = start.ArgumentList.ToArray();
                Assert.Equal(new[] { "/usr/bin/pacman", "-U", "--noconfirm", "--", path }, arguments.TakeLast(5));
                Assert.DoesNotContain("test-password", arguments);
                Assert.False(start.UseShellExecute);
                return Task.FromResult(exitCode);
            });
            Assert.True(called);
            Assert.Equal(exitCode == 0, result);
        }
        finally
        {
            UpdateManager.CurPlatform = previousPlatform;
            UpdateManager.RootPassword = previousPassword;
        }
    }

    [Theory]
    [InlineData(false, ".pkg.tar.zst")]
    [InlineData(true, ".pkg.tar.zst")]
    [InlineData(false, ".pacman")]
    [InlineData(true, ".pacman")]
    public async Task NativeArchPackagesInstallInsteadOfExtractingAndKeepLocalCopies(bool success, string extension)
    {
        if (!InstallationManager.CanInstallArchPackages) return;
        string root = Path.Combine(Path.GetTempPath(), "ghd-arch-package-" + Guid.NewGuid().ToString("N"));
        HistoryStore previous = OperationHistory.Store;
        LocalAppStore previousApps = LocalApps.Store;
        Directory.CreateDirectory(root);
        try
        {
            OperationHistory.Store = new(Path.Combine(root, "history.json"));
            LocalApps.Store = new(Path.Combine(root, "apps.json"));
            string path = Path.Combine(root, "app" + extension);
            Assert.Contains(extension, LocalFileInstaller.SupportedExtensions);
            await File.WriteAllTextAsync(path, "package content");
            Assert.True(UpdateManager.IsInstallerAsset(path));
            Assert.False(ArchiveExtractor.IsSupportedArchive(path));
            Assert.False(ArchiveExtractor.IsSupportedArchive("app.PKG.TAR.ZST"));
            Assert.True(ArchiveExtractor.IsSupportedArchive("ordinary.tar.zst"));
            bool installed = false;
            bool result = await LocalFileInstaller.InstallAsync(path, _ => { }, _ => { }, null, asset =>
            {
                installed = true;
                Assert.Equal("package content", File.ReadAllText(asset.TempAssetPath));
                return Task.FromResult(success);
            }, Path.Combine(root, "cache"));
            Assert.True(installed);
            Assert.Equal(success, result);
            Assert.Equal("package content", File.ReadAllText(path));
            if (success)
            {
                LocalApp app = Assert.Single(LocalApps.Store.Read());
                Assert.Equal(LocalAppKind.Arch, app.Kind);
                Assert.Equal("Arch package", app.Type);
                Assert.True(app.CanReinstall);
            }
            else Assert.Empty(LocalApps.Store.Read());

            Repo repo = new() { Url = "", Name = "owner/arch", AutoExtract = true, Tag = "v2" };
            repo.ReplaceAssets(["app" + extension], ["https://example.invalid/app" + extension]);
            repo.DownloadAssetIndex = 0;
            Assert.False(repo.IsDownloadOnly);
            bool applied = await UpdateManager.ApplyAssetAsync(new(repo, path, "v2"), _ => { }, _ => { }, false,
                _ => Task.FromResult(success));
            Assert.Equal(success, applied);
            Assert.Equal(success ? "v2" : "", repo.CurrentInstallTag);
            Assert.DoesNotContain(OperationHistory.Store.Query().Entries, entry => entry.Action == HistoryAction.Extract);
        }
        finally
        {
            OperationHistory.Store = previous;
            LocalApps.Store = previousApps;
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("package.deb", true, true, true)]
    [InlineData("package.DEB", true, false, false)]
    [InlineData("package.deb", false, true, false)]
    [InlineData("package.AppImage", true, false, true)]
    [InlineData("package.exe", true, false, true)]
    [InlineData("package.msi", false, false, true)]
    [InlineData("package.zip", true, true, false)]
    public void DebianInstallersRequireApt(string name, bool linux, bool apt, bool expected)
    {
        Assert.Equal(expected, UpdateManager.IsInstallerAsset(name, linux, apt));
    }

    [Fact]
    public void LocalInstallerPickerMatchesDebianInstallationSupport()
    {
        Assert.Equal(InstallationManager.CanInstallDebianPackages,
            LocalFileInstaller.SupportedExtensions.Contains(".deb"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutAptDebianUpdatesSaveFilesWithoutCallingInstallers(bool batch)
    {
        if (!OperatingSystem.IsLinux() || InstallationManager.CanInstallDebianPackages) return;
        string root = Path.Combine(Path.GetTempPath(), "ghd-package-manager-" + Guid.NewGuid().ToString("N"));
        HistoryStore previous = OperationHistory.Store;
        Directory.CreateDirectory(root);
        try
        {
            OperationHistory.Store = new(Path.Combine(root, "history.json"));
            string destination = Path.Combine(root, "downloads");
            Directory.CreateDirectory(destination);
            Repo repo = new()
            {
                Url = "https://github.com/owner/repo", Name = "owner/repo", Tag = "v2",
                CurrentInstallTag = "v1", DownloadPath = destination
            };
            repo.ReplaceAssets(["release.deb"], ["https://example.invalid/release.deb"]);
            repo.DownloadAssetIndex = 0;
            Assert.True(repo.IsDownloadOnly);
            Assert.False(repo.CanUninstall);
            Assert.False(repo.NeedsDebianPackageName);
            bool installerCalled = false;
            Task Download(string url, string path, IProgress<double> progress) => File.WriteAllTextAsync(path, "package content");
            Task<bool> Install(UpdateManager.Asset asset)
            {
                installerCalled = true;
                return Task.FromResult(false);
            }
            bool success;
            if (batch)
                success = await UpdateManager.RunBatchAsync([repo], _ => { }, _ => { }, false, false,
                    downloadFile: Download, cachePath: root, installDebianAssets: _ =>
                    {
                        installerCalled = true;
                        return Task.FromResult(false);
                    });
            else
                success = await UpdateManager.RunRepositoryAsync(repo, _ => { }, _ => { }, false,
                    downloadFile: Download, installAsset: Install, cachePath: root) == HistoryStatus.Succeeded;
            Assert.True(success);
            Assert.False(installerCalled);
            Assert.Equal("package content", await File.ReadAllTextAsync(Path.Combine(destination, "release.deb")));
            Assert.Equal("v2", repo.LastDownloadTag);
            Assert.Equal("v1", repo.CurrentInstallTag);
            Assert.True(repo.IsUpToDate);
            Assert.Contains(OperationHistory.Store.Query().Entries, entry => entry.Outcome == HistoryOutcome.Saved);
            Assert.DoesNotContain(OperationHistory.Store.Query().Entries, entry => entry.Action == HistoryAction.Install);
            Assert.Empty(Directory.GetDirectories(root, "archive-*"));
        }
        finally
        {
            OperationHistory.Store = previous;
            Directory.Delete(root, recursive: true);
        }
    }
}
