using GithubDownloader.Core.LocalApplications;
using GithubDownloader.Core.Installation;
using GithubDownloader.Core.Operations;
using GithubDownloader.Core.History;
using System.Diagnostics;
using System.Text.Json;
using GithubDownloader.Core;
using GithubDownloader.Core.Models;
using Xunit;

namespace GithubDownloader.Tests;

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

    private async Task<(string AppImage, string DesktopEntry)> CreateAppImage(string id)
    {
        string directory = Path.Combine(_root, "images", id);
        Directory.CreateDirectory(directory);
        string appImage = Path.Combine(directory, id + ".AppImage");
        string icon = Path.Combine(directory, "icon.png");
        string desktop = Path.Combine(_root, "desktop", id + ".desktop");
        Directory.CreateDirectory(Path.GetDirectoryName(desktop)!);
        await File.WriteAllTextAsync(appImage, "executable");
        await File.WriteAllTextAsync(icon, "icon");
        await File.WriteAllTextAsync(desktop, "[Desktop Entry]\nExec=" + appImage + "\n");
        return (appImage, desktop);
    }

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
    public async Task ArchInstallRemovesPreviousArchiveInstallation()
    {
        if (!OperatingSystem.IsLinux()) return;
        string extraction = Path.Combine(_root, "extracted");
        Directory.CreateDirectory(extraction);
        string executable = Path.Combine(extraction, "app");
        await File.WriteAllTextAsync(executable, "old app");
        string changedFile = Path.Combine(extraction, "changed.txt");
        await File.WriteAllTextAsync(changedFile, "user edit");
        string desktopDirectory = Path.Combine(_root, "desktop");
        Directory.CreateDirectory(desktopDirectory);
        string entry = Path.Combine(desktopDirectory, "owner_repo.desktop");
        await File.WriteAllTextAsync(entry, "[Desktop Entry]\nName=Old app\n");

        LocalApp archive = new()
        {
            Id = "repository:owner/repo",
            FileName = "release.tar.gz",
            InstallationName = "owner/repo",
            Kind = LocalAppKind.Archive,
            ExtractionDirectory = extraction,
            ExecutablePath = executable,
            DesktopEntryHash = LocalAppManager.FileHash(entry),
            OwnedFiles = new Dictionary<string, string>
            {
                ["app"] = LocalAppManager.FileHash(executable),
                ["changed.txt"] = new string('0', 64)
            }
        };
        Repo repo = new() { Url = "", Name = "owner/repo", CurrentInstallTag = "v1", ArchiveInstallation = archive };
        string package = Path.Combine(_root, "new.pkg.tar.zst");
        await File.WriteAllTextAsync(package, "installer");
        RepositoryAsset asset = new(repo, package, "v2");
        InstallationManager manager = Manager(start =>
        {
            if (start.ArgumentList.FirstOrDefault() == "-Qp") return new(0, "actual-package\n");
            Assert.Equal(new[] { "-U", "--noconfirm", "--", package }, start.ArgumentList.TakeLast(4));
            return new(0, "installed");
        });

        Assert.True(await UpdateManager.ApplyAssetAsync(asset, _ => { }, _ => { }, false,
            item => UpdateManager.InstallArchAssetAsync(item, _ => { }, manager, (_, _) => Task.FromResult(0)),
            installations: manager));

        Assert.Equal("actual-package", repo.InstalledArchPackage);
        Assert.Equal("v2", repo.CurrentInstallTag);
        Assert.False(repo.ExcludedFromDownloadAll);
        Assert.False(File.Exists(executable));
        Assert.True(File.Exists(changedFile));
        Assert.False(File.Exists(entry));
        Assert.False(repo.ArchiveInstallation!.IsInstalled);
        Assert.Contains(OperationHistory.Store.Query().Entries,
            item => item.Action == HistoryAction.Uninstall && item.Outcome == HistoryOutcome.Uninstalled);
    }

    [Fact]
    public async Task ArchInstallRemovesDebianAndAppImageInstallations()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = new()
        {
            Url = "", Name = "owner/repo", CurrentInstallTag = "v1",
            InstalledDebianPackage = "old-deb:amd64", InstalledAppImageId = "owner-repo"
        };
        (string appImage, string desktop) = await CreateAppImage("owner-repo");
        string package = Path.Combine(_root, "new.pkg.tar.zst");
        await File.WriteAllTextAsync(package, "installer");
        List<ProcessStartInfo> commands = [];
        InstallationManager manager = Manager(start =>
        {
            commands.Add(start);
            if (start.ArgumentList.FirstOrDefault() == "-Qp") return new(0, "new-arch\n");
            return new(0, "");
        });
        RepositoryAsset asset = new(repo, package, "v2");

        Assert.True(await UpdateManager.ApplyAssetAsync(asset, _ => { }, _ => { }, false,
            item => UpdateManager.InstallArchAssetAsync(item, _ => { }, manager, (_, _) => Task.FromResult(0)),
            installations: manager));

        Assert.Contains(commands, start => start.ArgumentList.TakeLast(4)
            .SequenceEqual(new[] { "/usr/bin/dpkg", "--remove", "--", "old-deb:amd64" }));
        Assert.Empty(repo.InstalledDebianPackage);
        Assert.Empty(repo.InstalledAppImageId);
        Assert.Equal("new-arch", repo.InstalledArchPackage);
        Assert.Equal("v2", repo.CurrentInstallTag);
        Assert.False(repo.ExcludedFromDownloadAll);
        Assert.False(File.Exists(appImage));
        Assert.False(File.Exists(desktop));
        Assert.Equal(2, OperationHistory.Store.Query().Entries.Count(
            entry => entry.Action == HistoryAction.Uninstall && entry.Outcome == HistoryOutcome.Uninstalled));
    }

    [Fact]
    public async Task AppImageInstallRemovesDebianAndArchInstallations()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = new()
        {
            Url = "", Name = "owner/repo", CurrentInstallTag = "v1",
            InstalledDebianPackage = "old-deb:amd64", InstalledArchPackage = "old-arch"
        };
        string appImage = Path.Combine(_root, "new.AppImage");
        await File.WriteAllTextAsync(appImage, "installer");
        List<ProcessStartInfo> commands = [];
        InstallationManager manager = Manager(start =>
        {
            commands.Add(start);
            return new(0, "");
        });
        RepositoryAsset asset = new(repo, appImage, "v2");

        Assert.True(await UpdateManager.ApplyAssetAsync(asset, _ => { }, _ => { }, false,
            item =>
            {
                item.Repo.InstalledAppImageId = "owner-repo";
                return Task.FromResult(true);
            }, installations: manager));

        Assert.Contains(commands, start => start.ArgumentList.TakeLast(4)
            .SequenceEqual(new[] { "/usr/bin/dpkg", "--remove", "--", "old-deb:amd64" }));
        Assert.Contains(commands, start => start.ArgumentList.TakeLast(4)
            .SequenceEqual(new[] { "-R", "--noconfirm", "--", "old-arch" }));
        Assert.Empty(repo.InstalledDebianPackage);
        Assert.Empty(repo.InstalledArchPackage);
        Assert.Equal("owner-repo", repo.InstalledAppImageId);
        Assert.Equal("v2", repo.CurrentInstallTag);
        Assert.False(repo.ExcludedFromDownloadAll);
    }

    [Fact]
    public async Task SameKindInstallKeepsThePreviousInstallationRecord()
    {
        if (!InstallationManager.CanInstallDebianPackages) return;
        Repo repo = new()
        {
            Url = "", Name = "owner/repo", CurrentInstallTag = "v1", InstalledDebianPackage = "old-deb:amd64"
        };
        string deb = Path.Combine(_root, "new.deb");
        await File.WriteAllTextAsync(deb, "installer");
        List<ProcessStartInfo> commands = [];
        InstallationManager manager = Manager(start =>
        {
            commands.Add(start);
            return new(0, "");
        });
        RepositoryAsset asset = new(repo, deb, "v2");

        Assert.True(await UpdateManager.ApplyAssetAsync(asset, _ => { }, _ => { }, false,
            item =>
            {
                item.Repo.InstalledDebianPackage = "new-deb:amd64";
                return Task.FromResult(true);
            }, installations: manager));

        Assert.Empty(commands);
        Assert.Equal("new-deb:amd64", repo.InstalledDebianPackage);
        Assert.Equal("v2", repo.CurrentInstallTag);
        Assert.False(repo.ExcludedFromDownloadAll);
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
