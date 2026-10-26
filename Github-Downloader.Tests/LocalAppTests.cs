using System.IO.Compression;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class LocalAppTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghd-local-apps-test-" + Guid.NewGuid().ToString("N"));
    private readonly HistoryStore _previousHistory = OperationHistory.Store;
    private readonly LocalAppStore _previousApps = LocalApps.Store;
    private string AppsPath => Path.Combine(_root, "local-apps.json");

    public LocalAppTests()
    {
        Directory.CreateDirectory(_root);
        OperationHistory.Store = new(Path.Combine(_root, "history.json"));
        LocalApps.Store = new(AppsPath);
    }

    [Fact]
    public async Task ArchiveCanBeReinstalledAfterOriginalAndCacheAreDeleted()
    {
        string source = Path.Combine(_root, "archive.zip");
        using (ZipArchive archive = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            using StreamWriter writer = new(archive.CreateEntry("app.txt").Open());
            writer.Write("original content");
        }
        string destination = Path.Combine(OperatingSystem.IsLinux() ? LocalApps.Store.AppsPath : _root, "app");
        string cache = Path.Combine(_root, "cache");
        Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, destination, null, cache));
        LocalApp app = Assert.Single(new LocalAppStore(AppsPath).Read());
        File.Delete(source);
        Directory.Delete(cache, true);
        File.Delete(Path.Combine(destination, "app.txt"));
        // Use the test cache while exercising the same saved source and extraction destination as the UI.
        Assert.True(await LocalFileInstaller.InstallAsync(app.InstallerPath, _ => { }, _ => { }, app.ExtractionDirectory, null, cache));
        Assert.Equal("original content", await File.ReadAllTextAsync(Path.Combine(destination, "app.txt")));
        LocalApp reinstalled = Assert.Single(LocalApps.Store.Read());
        Assert.Equal(app.Id, reinstalled.Id);
        Assert.Equal(app.InstallerPath, reinstalled.InstallerPath);
        if (!OperatingSystem.IsLinux())
        {
            Assert.False(reinstalled.CanUninstall);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new LocalAppManager().UninstallAsync(reinstalled, _ => { }, _ => { }));
            Assert.Equal("original content", await File.ReadAllTextAsync(Path.Combine(destination, "app.txt")));
            return;
        }
        Assert.True(await new LocalAppManager().UninstallAsync(reinstalled, _ => { }, _ => { }));
        Assert.True(File.Exists(app.InstallerPath));
        LocalApp uninstalled = Assert.Single(new LocalAppStore(AppsPath).Read());
        Assert.False(uninstalled.IsInstalled);
        Assert.False(uninstalled.CanUninstall);
        Assert.True(uninstalled.CanReinstall);
        Assert.True(await LocalFileInstaller.InstallAsync(uninstalled.InstallerPath, _ => { }, _ => { },
            uninstalled.ExtractionDirectory, null, cache));
        Assert.True(Assert.Single(LocalApps.Store.Read()).IsInstalled);
        Assert.Equal("original content", await File.ReadAllTextAsync(Path.Combine(destination, "app.txt")));
    }

    [Fact]
    public async Task DebianInstallationPersistsExactPackageAcrossRestartAndReinstall()
    {
        if (!InstallationManager.CanInstallDebianPackages) return;
        foreach (string filename in new[] { "app-v1.deb", "app-v2.deb" })
        {
            string source = Path.Combine(_root, filename);
            await File.WriteAllTextAsync(source, "installer");
            Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null, asset =>
            {
                asset.Repo.InstalledDebianPackage = "example-app:amd64";
                return Task.FromResult(true);
            }, Path.Combine(_root, "cache")));
        }
        LocalApp app = Assert.Single(new LocalAppStore(AppsPath).Read());
        Assert.Equal("app-v2.deb", app.FileName);
        Assert.Equal("example-app:amd64", app.DebianPackage);
        Assert.True(app.CanUninstall);
        OperationHistory.Store.Clear();
        Assert.Single(new LocalAppStore(AppsPath).Read());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(126)]
    public async Task DebianRemovalUsesSavedIdentityAndRetainsFailedEntries(int exitCode)
    {
        if (!OperatingSystem.IsLinux()) return;
        string saved = Path.Combine(LocalApps.Store.InstallersPath, "example", "example.deb");
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        await File.WriteAllTextAsync(saved, "saved installer");
        LocalApp app = DebianApp() with { InstallerPath = saved };
        LocalApps.Store.Save(app);
        InstallationManager installations = new(Path.Combine(_root, "images"), Path.Combine(_root, "desktop"), (start, progress) =>
        {
            Assert.Contains("--remove", start.ArgumentList);
            Assert.Equal("example-app:amd64", start.ArgumentList.Last());
            return Task.FromResult(new InstallationManager.CommandResult(exitCode, "private output"));
        });
        bool success = await new LocalAppManager(LocalApps.Store, installations).UninstallAsync(app, _ => { }, _ => { });
        Assert.Equal(exitCode == 0, success);
        Assert.Equal(exitCode != 0, Assert.Single(new LocalAppStore(AppsPath).Read()).IsInstalled);
        Assert.Equal("saved installer", await File.ReadAllTextAsync(saved));
        HistoryEntry history = Assert.Single(OperationHistory.Store.Query().Entries);
        Assert.Equal(app.FileName, history.LocalFileName);
        Assert.Equal(exitCode == 0 ? HistoryStatus.Succeeded : HistoryStatus.Failed, history.Status);
        Assert.Empty(history.RepositoryName);
    }

    [Fact]
    public async Task AppImageRemovalDeletesOnlyTheManagedInstallationAndMenuEntry()
    {
        if (!OperatingSystem.IsLinux()) return;
        string images = Path.Combine(_root, "images");
        string desktop = Path.Combine(_root, "desktop");
        string id = "local-example-app";
        string managed = Path.Combine(images, id, id + ".AppImage");
        Directory.CreateDirectory(Path.GetDirectoryName(managed)!);
        Directory.CreateDirectory(desktop);
        await File.WriteAllTextAsync(managed, "installed");
        await File.WriteAllTextAsync(Path.Combine(images, id, "icon.png"), "icon");
        await File.WriteAllTextAsync(Path.Combine(desktop, id + ".desktop"), "Exec=" + managed);
        string original = Path.Combine(_root, "example.AppImage");
        await File.WriteAllTextAsync(original, "original");
        LocalApp app = DebianApp() with { Kind = LocalAppKind.AppImage, DebianPackage = "", AppImageId = id, FileName = "example.AppImage" };
        LocalApps.Store.Save(app);
        InstallationManager installations = new(images, desktop, (_, _) => throw new Exception("No commands needed for AppImage removal."));
        Assert.True(await new LocalAppManager(LocalApps.Store, installations).UninstallAsync(app, _ => { }, _ => { }));
        Assert.False(Directory.Exists(Path.Combine(images, id)));
        Assert.False(File.Exists(Path.Combine(desktop, id + ".desktop")));
        Assert.Equal("original", await File.ReadAllTextAsync(original));
        Assert.False(Assert.Single(LocalApps.Store.Read()).IsInstalled);
    }

    [Fact]
    public async Task ArchivesExtractedOutsideManagedFolderHaveNoUninstallOrFileRecords()
    {
        if (!OperatingSystem.IsLinux()) return;
        string destination = Path.Combine(_root, "app");
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(destination, "existing.txt"), "preexisting");
        string source = Path.Combine(_root, "archive.zip");
        using (ZipArchive archive = ZipFile.Open(source, ZipArchiveMode.Create))
            foreach (string file in new[] { "existing.txt", "app.txt", "changed.txt" })
            {
                using StreamWriter writer = new(archive.CreateEntry(file).Open());
                writer.Write("installed");
            }
        Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, destination, null, Path.Combine(_root, "cache")));
        LocalApp app = Assert.Single(new LocalAppStore(AppsPath).Read());
        Assert.Null(app.OwnedFiles);
        Assert.DoesNotContain("OwnedFiles", await File.ReadAllTextAsync(AppsPath));
        await File.WriteAllTextAsync(Path.Combine(destination, "changed.txt"), "user changes");
        await File.WriteAllTextAsync(Path.Combine(destination, "settings.json"), "user settings");
        Assert.False(app.CanUninstall);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LocalAppManager().UninstallAsync(app, _ => { }, _ => { }));
        Assert.True(File.Exists(Path.Combine(destination, "app.txt")));
        Assert.Equal("user changes", await File.ReadAllTextAsync(Path.Combine(destination, "changed.txt")));
        Assert.Equal("user settings", await File.ReadAllTextAsync(Path.Combine(destination, "settings.json")));
        Assert.True(File.Exists(Path.Combine(destination, "existing.txt")));
        Assert.True(File.Exists(source));
        Assert.True(Assert.Single(LocalApps.Store.Read()).IsInstalled);
    }

    [Fact]
    public async Task ArchiveReinstallNeedsNoFileRecordsForUninstall()
    {
        if (!OperatingSystem.IsLinux()) return;
        string destination = Path.Combine(LocalApps.Store.AppsPath, "app");
        for (int version = 1; version <= 2; version++)
        {
            string source = Path.Combine(_root, $"app-v{version}.zip");
            using (ZipArchive archive = ZipFile.Open(source, ZipArchiveMode.Create))
            {
                using StreamWriter writer = new(archive.CreateEntry("app.txt").Open());
                writer.Write("version " + version);
            }
            Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, destination, null, Path.Combine(_root, "cache")));
        }
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        Assert.Equal("app-v2.zip", app.FileName);
        Assert.Null(app.OwnedFiles);
        Assert.True(await new LocalAppManager().UninstallAsync(app, _ => { }, _ => { }));
        Assert.False(File.Exists(Path.Combine(destination, "app.txt")));
    }

    [Fact]
    public async Task ArchiveRemovalOutsideManagedFolderIsRejectedEvenWithFileRecords()
    {
        if (!OperatingSystem.IsLinux()) return;
        string destination = Path.Combine(_root, "app");
        Directory.CreateDirectory(destination);
        string file = Path.Combine(destination, "app.txt");
        await File.WriteAllTextAsync(file, "keep");
        LocalApp app = DebianApp() with
        {
            Kind = LocalAppKind.Archive, ExtractionDirectory = destination,
            OwnedFiles = new() { ["app.txt"] = LocalAppManager.FileHash(file), ["../outside.txt"] = new string('A', 64) }
        };
        LocalApps.Store.Save(app);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LocalAppManager().UninstallAsync(app, _ => { }, _ => { }));
        Assert.Equal("keep", await File.ReadAllTextAsync(file));
        Assert.Single(LocalApps.Store.Read());
        if (OperatingSystem.IsWindows()) return;
        string outside = Path.Combine(_root, "outside.txt");
        await File.WriteAllTextAsync(outside, "outside");
        File.Delete(file);
        File.CreateSymbolicLink(file, outside);
        app = app with { OwnedFiles = new() { ["app.txt"] = LocalAppManager.FileHash(outside) } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LocalAppManager().UninstallAsync(app, _ => { }, _ => { }));
        Assert.Equal("outside", await File.ReadAllTextAsync(outside));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RemoveDeletesTheEntryAndSavedInstallerWhileKeepingInstalledAndImportedFiles(bool installed)
    {
        string original = Path.Combine(_root, "example.deb");
        string installedFile = Path.Combine(_root, "installed-app.txt");
        string saved = Path.Combine(LocalApps.Store.InstallersPath, "example", "example.deb");
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        File.WriteAllText(original, "imported");
        File.WriteAllText(installedFile, "installed");
        File.WriteAllText(saved, "saved installer");
        LocalApp app = DebianApp() with { InstallerPath = saved, IsInstalled = installed };
        LocalApps.Store.Save(app);

        LocalApps.Store.Remove(app.Id);

        Assert.Empty(new LocalAppStore(AppsPath).Read());
        Assert.False(File.Exists(saved));
        Assert.False(Directory.Exists(Path.GetDirectoryName(saved)));
        Assert.Equal("imported", File.ReadAllText(original));
        Assert.Equal("installed", File.ReadAllText(installedFile));
    }

    [Fact]
    public void RemoveKeepsInstallersUsedByAnotherEntryAndUnrelatedSavedFiles()
    {
        string saved = Path.Combine(LocalApps.Store.InstallersPath, "example", "example.deb");
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        File.WriteAllText(saved, "shared installer");
        string unrelated = Path.Combine(Path.GetDirectoryName(saved)!, "other.deb");
        File.WriteAllText(unrelated, "other installer");
        LocalApp first = DebianApp() with { InstallerPath = saved };
        LocalApp second = first with { Id = "another-entry" };
        LocalApps.Store.Save(first);
        LocalApps.Store.Save(second);

        LocalApps.Store.Remove(first.Id);
        Assert.Equal(second.Id, Assert.Single(LocalApps.Store.Read()).Id);
        Assert.True(File.Exists(saved));
        LocalApps.Store.Remove(second.Id);
        Assert.Empty(LocalApps.Store.Read());
        Assert.False(File.Exists(saved));
        Assert.Equal("other installer", File.ReadAllText(unrelated));
    }

    [Fact]
    public void RemoveAllowsMissingInstallersButRejectsOutsidePathsAndLinks()
    {
        LocalApp app = DebianApp() with { InstallerPath = Path.Combine(LocalApps.Store.InstallersPath, "missing.deb") };
        LocalApps.Store.Save(app);
        LocalApps.Store.Remove(app.Id);
        Assert.Empty(LocalApps.Store.Read());

        string outside = Path.Combine(_root, "original.deb");
        File.WriteAllText(outside, "keep");
        LocalApps.Store.Save(app with { InstallerPath = outside });
        Assert.Throws<IOException>(() => LocalApps.Store.Remove(app.Id));
        Assert.Single(LocalApps.Store.Read());
        Assert.Equal("keep", File.ReadAllText(outside));
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(LocalApps.Store.InstallersPath);
        string link = Path.Combine(LocalApps.Store.InstallersPath, "linked.deb");
        File.CreateSymbolicLink(link, outside);
        LocalApps.Store.Save(app with { InstallerPath = link });
        Assert.Throws<IOException>(() => LocalApps.Store.Remove(app.Id));
        Assert.Single(LocalApps.Store.Read());
        Assert.Equal("keep", File.ReadAllText(outside));
    }

    [Fact]
    public void CatalogOmitsFileRecordsForEveryLocalApp()
    {
        LocalApp managed = DebianApp() with
        {
            Kind = LocalAppKind.Archive, ExtractionDirectory = Path.Combine(LocalApps.Store.AppsPath, "example"),
            OwnedFiles = new() { ["app.txt"] = new string('A', 64) }
        };
        File.WriteAllText(AppsPath, System.Text.Json.JsonSerializer.Serialize(new[] { managed }));
        Assert.Null(Assert.Single(LocalApps.Store.Read()).OwnedFiles);
        LocalApps.Store.Save(managed);
        Assert.DoesNotContain("OwnedFiles", File.ReadAllText(AppsPath));
        LocalApps.Store.Save(DebianApp() with { OwnedFiles = managed.OwnedFiles });
        Assert.DoesNotContain("OwnedFiles", File.ReadAllText(AppsPath));
        LocalApps.Store.Save(managed with { ExtractionDirectory = Path.Combine(_root, "chosen") });
        Assert.DoesNotContain("OwnedFiles", File.ReadAllText(AppsPath));
    }

    [Fact]
    public void ExistingEntriesDefaultToInstalled()
    {
        File.WriteAllText(AppsPath, """
            [{"Id":"example","FileName":"example.zip","InstallationName":"local/example","Kind":"Archive"}]
            """);
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        Assert.True(app.IsInstalled);
        Assert.False(app.CanUninstall);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[null]")]
    public void CorruptLocalAppListsArePreserved(string json)
    {
        File.WriteAllText(AppsPath, json);
        Assert.Throws<InvalidDataException>(() => LocalApps.Store.Read());
        Assert.Throws<InvalidDataException>(() => LocalApps.Store.Save(DebianApp()));
        Assert.Equal(json, File.ReadAllText(AppsPath));
    }

    private static LocalApp DebianApp() => new()
    {
        Id = "deb:example-app:amd64", FileName = "example.deb", InstallationName = "local/example",
        DebianPackage = "example-app:amd64", Kind = LocalAppKind.Debian, InstalledAt = DateTimeOffset.UtcNow
    };

    public void Dispose()
    {
        OperationHistory.Store = _previousHistory;
        LocalApps.Store = _previousApps;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
