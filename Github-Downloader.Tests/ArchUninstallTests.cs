using System.Diagnostics;
using System.Text.Json;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class ArchUninstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghd-arch-uninstall-" + Guid.NewGuid().ToString("N"));
    private readonly HistoryStore _previousHistory = OperationHistory.Store;
    private readonly LocalAppStore _previousApps = LocalApps.Store;

    public ArchUninstallTests()
    {
        Directory.CreateDirectory(_root);
        OperationHistory.Store = new(Path.Combine(_root, "history.json"));
        LocalApps.Store = new(Path.Combine(_root, "apps.json"));
    }

    private InstallationManager Manager(Func<ProcessStartInfo, InstallationManager.CommandResult> run) =>
        new(Path.Combine(_root, "images"), Path.Combine(_root, "desktop"), (start, _) => Task.FromResult(run(start)));

    [Theory]
    [InlineData("actual-package\n", 0, true)]
    [InlineData("libexample++\n", 0, true)]
    [InlineData("--remove", 0, false)]
    [InlineData("one\ntwo\n", 0, false)]
    [InlineData("package; command", 0, false)]
    [InlineData("package*", 0, false)]
    [InlineData("", 0, false)]
    [InlineData("actual-package", 1, false)]
    public async Task PackageIdentityComesFromValidatedMetadata(string output, int exit, bool valid)
    {
        string path = Path.Combine(_root, "unrelated-name.pkg.tar.zst");
        InstallationManager manager = Manager(start =>
        {
            Assert.Equal("/usr/bin/pacman", start.FileName);
            Assert.Equal(new[] { "-Qp", "--quiet", "--", path }, start.ArgumentList);
            return new(exit, output);
        });
        if (valid) Assert.Equal(output.Trim(), await manager.ReadArchPackageNameAsync(path));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadArchPackageNameAsync(path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(126)]
    public async Task UninstallUsesSavedIdentityAfterAssetChangesAndOnlyClearsStateOnSuccess(int exitCode)
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo original = new() { Url = "", Name = "owner/repo", CurrentInstallTag = "v1", InstalledArchPackage = "actual-package" };
        original.ReplaceAssets(["different.zip"], ["https://example.invalid/different.zip"]);
        Repo repo = JsonSerializer.Deserialize<Repo>(JsonSerializer.Serialize(original))!;
        Assert.True(repo.CanUninstall);
        Assert.False(repo.NeedsPackageName);
        InstallationManager manager = Manager(start =>
        {
            Assert.Equal(new[] { "/usr/bin/pacman", "-R", "--noconfirm", "--", "actual-package" }, start.ArgumentList.TakeLast(5));
            Assert.False(start.UseShellExecute);
            return new(exitCode, "private output");
        });
        Assert.Contains("actual-package", manager.GetUninstallDescription(repo));
        Assert.Equal(exitCode == 0, await manager.UninstallAsync(repo, _ => { }, _ => { }));
        Assert.Equal(exitCode == 0 ? "" : "actual-package", repo.InstalledArchPackage);
        Assert.Equal(exitCode == 0 ? "" : "v1", repo.CurrentInstallTag);
        Assert.Equal(exitCode == 0, repo.ExcludedFromDownloadAll);
        Assert.Equal(exitCode == 0 ? HistoryOutcome.Uninstalled : HistoryOutcome.UninstallFailed,
            Assert.Single(OperationHistory.Store.Query().Entries).Outcome);
    }

    [Theory]
    [InlineData(0, ".pkg.tar.zst")]
    [InlineData(1, ".pkg.tar.zst")]
    [InlineData(0, ".pacman")]
    [InlineData(1, ".pacman")]
    public async Task InstallationSavesIdentityOnlyOnSuccessAndLocalUninstallKeepsSavedInstallers(int installExit, string extension)
    {
        if (!InstallationManager.CanInstallArchPackages) return;
        InstallationManager metadata = Manager(_ => new(0, "actual-package\n"));
        for (int version = 1; version <= 2; version++)
        {
            string source = Path.Combine(_root, $"different-v{version}{extension}");
            await File.WriteAllTextAsync(source, "installer");
            Assert.Equal(installExit == 0, await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null,
                asset => UpdateManager.InstallArchAssetAsync(asset, _ => { }, metadata, (_, _) => Task.FromResult(installExit)),
                Path.Combine(_root, "cache")));
        }
        if (installExit != 0) { Assert.Empty(LocalApps.Store.Read()); return; }
        LocalApp app = Assert.Single(new LocalAppStore(Path.Combine(_root, "apps.json")).Read());
        Assert.Equal("arch:actual-package", app.Id);
        Assert.Equal("actual-package", app.ArchPackage);
        Assert.True(app.CanUninstall);
        Assert.Equal(2, app.AvailableInstallerVersions.Count);
        InstallationManager removal = Manager(start =>
        {
            Assert.Equal("actual-package", start.ArgumentList.Last());
            return new(0, "");
        });
        Assert.True(await new LocalAppManager(LocalApps.Store, removal).UninstallAsync(app, _ => { }, _ => { }));
        LocalApp removed = Assert.Single(LocalApps.Store.Read());
        Assert.False(removed.IsInstalled);
        Assert.False(removed.CanUninstall);
        Assert.True(removed.CanReinstall);
        Assert.All(app.AvailableInstallerVersions, version => Assert.True(File.Exists(version.Path)));
    }

    [Fact]
    public async Task LegacyArchInstallsRequireAnExplicitValidPackageName()
    {
        if (!InstallationManager.CanInstallArchPackages) return;
        Repo repo = new() { Url = "", Name = "owner/repo", CurrentInstallTag = "v1" };
        repo.ReplaceAssets(["unknown.pkg.tar.zst"], ["https://example.invalid/package"]);
        repo.DownloadAssetIndex = 0;
        Assert.True(repo.NeedsArchPackageName);
        Assert.True(repo.NeedsPackageName);
        int calls = 0;
        InstallationManager manager = Manager(start =>
        {
            calls++;
            Assert.Equal("actual-package", start.ArgumentList.Last());
            return new(0, "");
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UninstallAsync(repo, _ => { }, _ => { }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UninstallAsync(repo, _ => { }, _ => { }, "--all"));
        Assert.Equal(0, calls);
        Assert.True(await manager.UninstallAsync(repo, _ => { }, _ => { }, " actual-package "));
        Assert.Equal(1, calls);
    }

    public void Dispose()
    {
        OperationHistory.Store = _previousHistory;
        LocalApps.Store = _previousApps;
        Directory.Delete(_root, recursive: true);
    }
}
