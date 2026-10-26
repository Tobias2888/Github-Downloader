using System.IO.Compression;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class LocalInstallerRetentionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghd-retention-" + Guid.NewGuid().ToString("N"));
    private readonly LocalAppStore _previousApps = LocalApps.Store;
    private readonly HistoryStore _previousHistory = OperationHistory.Store;
    private string Cache => Path.Combine(_root, "cache");

    public LocalInstallerRetentionTests()
    {
        Directory.CreateDirectory(_root);
        LocalApps.Store = new(Path.Combine(_root, "local-apps.json"));
        OperationHistory.Store = new(Path.Combine(_root, "history.json"));
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public async Task ArchiveVersionsKeepTheNewestCopiesWithoutChangingOriginalsOrInstalledFiles(int? limit, int expected)
    {
        string destination = Path.Combine(_root, "application");
        List<string> savedPaths = [];
        for (int version = 1; version <= 3; version++)
        {
            string source = Path.Combine(_root, $"application-v{version}.zip");
            using (ZipArchive archive = ZipFile.Open(source, ZipArchiveMode.Create))
            {
                using StreamWriter writer = new(archive.CreateEntry("app.txt").Open());
                writer.Write($"version {version}");
            }
            Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, destination, null, Cache, limit));
            LocalApp current = Assert.Single(LocalApps.Store.Read());
            if (current.InstallerPath.Length > 0) savedPaths.Add(current.InstallerPath);
        }

        LocalApp app = Assert.Single(new LocalAppStore(Path.Combine(_root, "local-apps.json")).Read());
        Assert.Equal(expected, app.InstallerVersions.Count);
        Assert.True(app.IsInstalled);
        Assert.Equal(expected > 0, app.CanReinstall);
        Assert.Equal("version 3", File.ReadAllText(Path.Combine(destination, "app.txt")));
        for (int version = 1; version <= 3; version++)
            Assert.True(File.Exists(Path.Combine(_root, $"application-v{version}.zip")));
        if (expected == 0)
        {
            Assert.Empty(app.InstallerPath);
            Assert.False(Directory.Exists(LocalApps.Store.InstallersPath));
        }
        else
        {
            Assert.EndsWith("application-v3.zip", app.InstallerPath);
            Assert.Equal(savedPaths.TakeLast(expected).Reverse(), app.InstallerVersions.Select(version => version.Path));
            Assert.Equal(expected, Directory.GetFiles(LocalApps.Store.InstallersPath, "*", SearchOption.AllDirectories).Length);
        }
    }

    [Fact]
    public async Task FailedInstallsDoNotPruneAndReinstallingASavedVersionDoesNotCreateDuplicates()
    {
        string source = Path.Combine(_root, "application.msi");
        for (int version = 1; version <= 2; version++)
        {
            await File.WriteAllTextAsync(source, $"version {version}");
            Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null,
                _ => Task.FromResult(true), Cache, 2));
        }
        LocalApp before = Assert.Single(LocalApps.Store.Read());
        Assert.Equal(2, before.InstallerVersions.Count);
        Assert.False(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null,
            _ => Task.FromResult(false), Cache, 0));
        Assert.Equal(before.InstallerVersions, Assert.Single(LocalApps.Store.Read()).InstallerVersions);
        Assert.True(await LocalFileInstaller.InstallAsync(before.InstallerPath, _ => { }, _ => { }, null,
            _ => Task.FromResult(true), Cache, 2));
        LocalApp after = Assert.Single(LocalApps.Store.Read());
        Assert.Equal(before.InstallerVersions, after.InstallerVersions);
        Assert.Equal(2, Directory.GetFiles(LocalApps.Store.InstallersPath, "*", SearchOption.AllDirectories).Length);

        LocalApps.Store.Remove(after.Id);
        Assert.Empty(LocalApps.Store.Read());
        Assert.Empty(Directory.GetFiles(LocalApps.Store.InstallersPath, "*", SearchOption.AllDirectories));
        Assert.Equal("version 2", File.ReadAllText(source));
    }

    [Fact]
    public async Task DebianVersionsWithDifferentFilenamesShareThePackageRetentionLimit()
    {
        if (!InstallationManager.CanInstallDebianPackages) return;
        string? first = null;
        for (int version = 1; version <= 2; version++)
        {
            string source = Path.Combine(_root, $"application-v{version}.deb");
            await File.WriteAllTextAsync(source, $"version {version}");
            Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null, asset =>
            {
                asset.Repo.InstalledDebianPackage = "example-app:amd64";
                return Task.FromResult(true);
            }, Cache, 1));
            if (version == 1) first = Assert.Single(LocalApps.Store.Read()).InstallerPath;
        }
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        Assert.Single(app.InstallerVersions);
        Assert.False(File.Exists(first));
        Assert.EndsWith("application-v2.deb", app.InstallerPath);
    }

    [Fact]
    public async Task InstallingAnOlderSavedArchiveUpdatesTheSameEntryAndReinstallKeepsTheSelectedVersion()
    {
        string destination = Path.Combine(_root, "application");
        for (int version = 1; version <= 2; version++)
        {
            string source = Path.Combine(_root, $"app-v{version}.zip");
            using (ZipArchive archive = ZipFile.Open(source, ZipArchiveMode.Create))
            {
                using StreamWriter writer = new(archive.CreateEntry("application.txt").Open());
                writer.Write($"version {version}");
            }
            Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, destination, null, Cache, 2));
        }
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        LocalInstallerVersion older = app.AvailableInstallerVersions.Last();
        LocalAppManager manager = new();
        Assert.True(await manager.InstallVersionAsync(app, older, _ => { }, _ => { }));
        LocalApp rolledBack = Assert.Single(LocalApps.Store.Read());
        Assert.Equal(app.Id, rolledBack.Id);
        Assert.Equal("app-v1.zip", rolledBack.FileName);
        Assert.Equal(older.Path, rolledBack.InstallerPath);
        Assert.Equal("version 1", File.ReadAllText(Path.Combine(destination, "application.txt")));
        Assert.Equal(app.InstallerVersions, rolledBack.InstallerVersions);
        Assert.All(app.InstallerVersions, version => Assert.True(File.Exists(version.Path)));
        File.WriteAllText(Path.Combine(destination, "application.txt"), "changed");
        Assert.True(await manager.ReinstallAsync(rolledBack, _ => { }, _ => { }));
        Assert.Equal("version 1", File.ReadAllText(Path.Combine(destination, "application.txt")));
        Assert.Equal(older.Path, Assert.Single(LocalApps.Store.Read()).InstallerPath);
    }

    [Fact]
    public async Task VersionInstallationRejectsMissingAndUnrecordedInstallers()
    {
        LocalApp app = SaveLegacyApp("example", installed: true);
        Assert.False(app.CanChooseInstallerVersion);
        LocalAppManager manager = new();
        LocalInstallerVersion unknown = new(Path.Combine(_root, "outside.msi"), DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<IOException>(() => manager.InstallVersionAsync(app, unknown, _ => { }, _ => { }));
        File.Delete(app.InstallerPath);
        Assert.Empty(app.AvailableInstallerVersions);
        await Assert.ThrowsAsync<IOException>(() => manager.InstallVersionAsync(app,
            new(app.InstallerPath, app.InstalledAt), _ => { }, _ => { }));
        Assert.Single(LocalApps.Store.Read());
    }

    [Fact]
    public void ApplyingALimitAdoptsLegacyCopiesKeepsEntriesAndHandlesUninstalledApps()
    {
        LocalApp first = SaveLegacyApp("first", installed: true);
        LocalApp second = SaveLegacyApp("second", installed: false);
        LocalApps.Store.ApplyInstallerRetention(1);
        Assert.All(LocalApps.Store.Read(), app => Assert.Single(app.InstallerVersions));
        LocalApps.Store.ApplyInstallerRetention(0);
        Assert.Equal(2, LocalApps.Store.Read().Count);
        Assert.True(LocalApps.Store.Read().Single(app => app.Id == first.Id).IsInstalled);
        Assert.False(LocalApps.Store.Read().Single(app => app.Id == second.Id).IsInstalled);
        Assert.All(LocalApps.Store.Read(), app =>
        {
            Assert.Empty(app.InstallerVersions);
            Assert.Empty(app.InstallerPath);
            Assert.False(app.CanReinstall);
        });
        Assert.False(File.Exists(first.InstallerPath));
        Assert.False(File.Exists(second.InstallerPath));
    }

    [Fact]
    public void PruningRejectsLinksBeforeDeletingAnyVersion()
    {
        if (OperatingSystem.IsWindows()) return;
        LocalApp first = SaveLegacyApp("first", installed: true);
        LocalApp second = SaveLegacyApp("second", installed: true);
        string original = Path.Combine(_root, "original.msi");
        File.WriteAllText(original, "keep original");
        File.Delete(second.InstallerPath);
        File.CreateSymbolicLink(second.InstallerPath, original);
        Assert.Throws<IOException>(() => LocalApps.Store.ApplyInstallerRetention(0));
        Assert.Equal(2, LocalApps.Store.Read().Count);
        Assert.True(File.Exists(first.InstallerPath));
        Assert.Equal("keep original", File.ReadAllText(original));
    }

    [Theory]
    [InlineData("{}", 2)]
    [InlineData("{\"LocalInstallerVersionsToKeep\":null}", null)]
    [InlineData("{\"LocalInstallerVersionsToKeep\":0}", 0)]
    [InlineData("{\"LocalInstallerVersionsToKeep\":5}", 5)]
    public void SharedSettingsReaderSupportsLegacyZeroAndUnlimited(string json, int? expected)
    {
        string settings = Path.Combine(_root, "appsettings.json");
        Assert.Equal(2, LocalInstallerRetention.LoadLimit(settings));
        File.WriteAllText(settings, json);
        Assert.Equal(expected, LocalInstallerRetention.LoadLimit(settings));
    }

    private LocalApp SaveLegacyApp(string name, bool installed)
    {
        string path = Path.Combine(LocalApps.Store.InstallersPath, name, name + ".msi");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "installer");
        LocalApp app = new()
        {
            Id = name, FileName = name + ".msi", InstallationName = "local/" + name,
            InstallerPath = path, IsInstalled = installed, Kind = LocalAppKind.Executable
        };
        LocalApps.Store.Save(app);
        return app;
    }

    public void Dispose()
    {
        LocalApps.Store = _previousApps;
        OperationHistory.Store = _previousHistory;
        Directory.Delete(_root, true);
    }
}
