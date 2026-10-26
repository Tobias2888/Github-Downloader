using System.IO.Compression;
using System.Diagnostics;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class ArchiveAppLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghd-archive-launcher-" + Guid.NewGuid().ToString("N"));
    private readonly LocalAppStore _previousApps = LocalApps.Store;
    private readonly HistoryStore _previousHistory = OperationHistory.Store;
    private string Destination => Path.Combine(_root, "app with spaces");
    private ArchiveAppLauncher Launcher => new(Path.Combine(_root, "applications"));

    public ArchiveAppLauncherTests()
    {
        Directory.CreateDirectory(_root);
        LocalApps.Store = new(Path.Combine(_root, "apps.json"));
        OperationHistory.Store = new(Path.Combine(_root, "history.json"));
    }

    private string Archive(params (string Name, string Content)[] files)
    {
        string path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in files)
        {
            using StreamWriter writer = new(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
        return path;
    }

    private Task<bool> Install(string archive, Func<string, IReadOnlyList<string>, Task<string?>>? select = null) =>
        LocalFileInstaller.InstallAsync(archive, _ => { }, _ => { }, Destination, null,
            Path.Combine(_root, "cache"), selectExecutable: select, launcher: Launcher);

    [Fact]
    public async Task ManagedAppUninstallRemovesRuntimeFilesAndTheEntireFolderWithoutFollowingLinks()
    {
        if (!OperatingSystem.IsLinux()) return;
        string source = Archive(("nested/app", "#!/bin/sh\n"));
        Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null, null,
            Path.Combine(_root, "cache"), launcher: Launcher));
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        string runtime = Path.Combine(app.ExtractionDirectory, "nested", ".config");
        Directory.CreateDirectory(runtime);
        await File.WriteAllTextAsync(Path.Combine(runtime, "generated.conf"), "generated settings");
        await File.AppendAllTextAsync(app.ExecutablePath, "# changed by the application\n");
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "keep.txt"), "outside data");
        Directory.CreateSymbolicLink(Path.Combine(app.ExtractionDirectory, "linked-directory"), outside);
        InstallationManager installations = new(_root, _root, (_, _) => throw new Exception("No commands needed."));
        LocalAppManager manager = new(LocalApps.Store, installations, Launcher);
        Assert.Contains("including files created by the app", manager.GetUninstallDescription(app));
        Assert.True(await manager.UninstallAsync(app, _ => { }, _ => { }));
        Assert.False(Directory.Exists(app.ExtractionDirectory));
        Assert.False(File.Exists(Launcher.EntryPath(app)));
        Assert.Equal("outside data", await File.ReadAllTextAsync(Path.Combine(outside, "keep.txt")));
        Assert.True(File.Exists(source));
        LocalApp removed = Assert.Single(LocalApps.Store.Read());
        Assert.False(removed.IsInstalled);
        Assert.False(removed.CanUninstall);
        Assert.True(removed.CanReinstall);
    }

    [Fact]
    public async Task PreviouslyUninstalledManagedAppsCanCleanUpLeftoverFolders()
    {
        if (!OperatingSystem.IsLinux()) return;
        string source = Archive(("nested/app", "#!/bin/sh\n"));
        Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null, null,
            Path.Combine(_root, "cache"), launcher: Launcher));
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        Launcher.Remove(app);
        File.Delete(app.ExecutablePath);
        await File.WriteAllTextAsync(Path.Combine(app.ExtractionDirectory, "nested", "generated.conf"), "leftover settings");
        LocalApps.Store.Save(app with { IsInstalled = false });
        LocalApp leftover = Assert.Single(LocalApps.Store.Read());
        Assert.False(leftover.IsInstalled);
        Assert.True(leftover.CanUninstall);
        InstallationManager installations = new(_root, _root, (_, _) => throw new Exception("No commands needed."));
        Assert.True(await new LocalAppManager(LocalApps.Store, installations, Launcher).UninstallAsync(leftover, _ => { }, _ => { }));
        Assert.False(Directory.Exists(leftover.ExtractionDirectory));
        Assert.False(Assert.Single(LocalApps.Store.Read()).CanUninstall);
    }

    [Fact]
    public async Task SingleExecutableCreatesLauncherAndUninstallRemovesItWhileKeepingChangedFilesAndArchive()
    {
        if (!OperatingSystem.IsLinux()) return;
        string source = Archive(("bin/launch app", "#!/bin/sh\nexit 0\n"), ("icon.png", "icon"), ("settings.txt", "original"));
        Assert.True(await Install(source, (_, _) => throw new Exception("A unique executable needs no picker.")));
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        Assert.Equal(Path.Combine(Destination, "bin", "launch app"), app.ExecutablePath);
        Assert.True((File.GetUnixFileMode(app.ExecutablePath) & UnixFileMode.UserExecute) != 0);
        string entry = Launcher.EntryPath(app);
        string content = await File.ReadAllTextAsync(entry);
        Assert.Contains("Exec=\"" + app.ExecutablePath + "\"", content);
        Assert.Contains("Path=" + Path.GetDirectoryName(app.ExecutablePath), content);
        Assert.Contains("Icon=" + Path.Combine(Destination, "icon.png"), content);
        Assert.Equal(LocalAppManager.FileHash(entry), app.DesktopEntryHash);
        if (File.Exists("/usr/bin/desktop-file-validate"))
        {
            using Process validator = new()
            {
                StartInfo = new("/usr/bin/desktop-file-validate")
                {
                    ArgumentList = { entry }, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
                }
            };
            validator.Start();
            Task<string> output = validator.StandardOutput.ReadToEndAsync();
            Task<string> errors = validator.StandardError.ReadToEndAsync();
            await validator.WaitForExitAsync();
            Assert.True(validator.ExitCode == 0, (await output) + (await errors));
        }
        await File.WriteAllTextAsync(Path.Combine(Destination, "settings.txt"), "user settings");
        InstallationManager installations = new(_root, _root, (_, _) => throw new Exception("No external commands are needed."));
        LocalAppManager manager = new(LocalApps.Store, installations, Launcher);
        Assert.True(await manager.UninstallAsync(app, _ => { }, _ => { }));
        Assert.False(File.Exists(entry));
        Assert.False(File.Exists(app.ExecutablePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(app.ExecutablePath)));
        Assert.Equal("user settings", await File.ReadAllTextAsync(Path.Combine(Destination, "settings.txt")));
        Assert.True(File.Exists(source));
        LocalApp removed = Assert.Single(LocalApps.Store.Read());
        Assert.True(removed.CanReinstall);
        Assert.False(removed.IsInstalled);
        Assert.True(await manager.ReinstallAsync(removed, _ => { }, _ => { }));
        Assert.True(File.Exists(entry));
        Assert.Equal(app.ExecutablePath, Assert.Single(LocalApps.Store.Read()).ExecutablePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrAmbiguousExecutableInvokesPickerAndReinstallsReuseItsSelection(bool multiple)
    {
        if (!OperatingSystem.IsLinux()) return;
        string source = multiple ? Archive(("app", "#!/bin/sh\n"), ("helper", "#!/bin/sh\n"))
            : Archive(("app", "custom executable format"));
        int picks = 0;
        Assert.True(await Install(source, (directory, candidates) =>
        {
            picks++;
            Assert.Equal(Destination, directory);
            Assert.Equal(multiple ? 2 : 0, candidates.Count);
            Assert.True(File.Exists(Path.Combine(directory, "app")));
            return Task.FromResult<string?>(Path.Combine(directory, "app"));
        }));
        Assert.Equal(1, picks);
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        Assert.True(await Install(app.InstallerPath, (_, _) => throw new Exception("Saved executable should be reused.")));
        Assert.Equal(app.ExecutablePath, Assert.Single(LocalApps.Store.Read()).ExecutablePath);
        Assert.True(File.Exists(Launcher.EntryPath(app)));
    }

    [Fact]
    public async Task CancellingExecutableSelectionKeepsTheExtractedAppWithoutALauncher()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(await Install(Archive(("README", "no executable")), (_, _) => Task.FromResult<string?>(null)));
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        Assert.Empty(app.ExecutablePath);
        Assert.Empty(app.DesktopEntryHash);
        Assert.False(File.Exists(Launcher.EntryPath(app)));
        Assert.True(File.Exists(Path.Combine(Destination, "README")));
        Assert.True(app.CanUninstall);
    }

    [Fact]
    public async Task ChangedDesktopEntriesAreKeptAndOutsideExecutablesAreRejected()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(await Install(Archive(("app", "#!/bin/sh\n"))));
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        string entry = Launcher.EntryPath(app);
        await File.WriteAllTextAsync(entry, "user-edited entry");
        Launcher.Remove(app);
        Assert.Equal("user-edited entry", await File.ReadAllTextAsync(entry));
        string outside = Path.Combine(_root, "outside");
        await File.WriteAllTextAsync(outside, "outside file");
        UnixFileMode mode = File.GetUnixFileMode(outside);
        LocalApp missing = app with { ExecutablePath = "", DesktopEntryHash = "" };
        await Assert.ThrowsAsync<IOException>(() => Launcher.ConfigureAsync(missing, null, [],
            (_, _) => Task.FromResult<string?>(outside), _ => { }));
        Assert.Equal(mode, File.GetUnixFileMode(outside));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReinstallAdoptsIdenticalLauncherAfterImportClearsOwnership(bool changed)
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(await Install(Archive(("app", "#!/bin/sh\n"))));
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        string entry = Launcher.EntryPath(app);
        if (changed) await File.AppendAllTextAsync(entry, "# user customization\n");
        string original = await File.ReadAllTextAsync(entry);
        LocalApp imported = app with
        {
            IsInstalled = false, ExtractionDirectory = "", ExecutablePath = "", DesktopEntryHash = "",
            OwnedFiles = new()
        };
        LocalApps.Store.Save(imported);
        if (changed)
            await Assert.ThrowsAsync<IOException>(() => Install(imported.InstallerPath));
        else
        {
            Assert.True(await Install(imported.InstallerPath));
            LocalApp installed = Assert.Single(LocalApps.Store.Read());
            Assert.True(installed.IsInstalled);
            Assert.Equal(app.ExecutablePath, installed.ExecutablePath);
            Assert.Equal(LocalAppManager.FileHash(entry), installed.DesktopEntryHash);
        }
        Assert.Equal(original, await File.ReadAllTextAsync(entry));
    }

    [Fact]
    public async Task LauncherFailureKeepsExtractedFilesAndTheSavedArchiveTracked()
    {
        if (!OperatingSystem.IsLinux()) return;
        string outside = Path.Combine(_root, "outside");
        await File.WriteAllTextAsync(outside, "outside file");
        await Assert.ThrowsAsync<IOException>(() => Install(Archive(("data", "extracted file")),
            (_, _) => Task.FromResult<string?>(outside)));
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        Assert.True(app.CanUninstall);
        Assert.True(app.CanReinstall);
        Assert.Contains("data", app.OwnedFiles.Keys);
        Assert.Empty(app.DesktopEntryHash);
        Assert.False(File.Exists(Launcher.EntryPath(app)));
    }

    [Fact]
    public void RepositoryLauncherFilenamesUseOwnerAndRepoWhileLocalAppsKeepUniqueSuffixes()
    {
        LocalApp repo = new() { Id = "repository:owner/My.App", FileName = "release-v2.zip", InstallationName = "owner/My.App" };
        string filename = Path.GetFileName(Launcher.EntryPath(repo));
        Assert.Equal("owner_my.app.desktop", filename);
        Assert.NotEqual(Launcher.EntryPath(repo with { Id = "repository:owner-a/repo" }),
            Launcher.EntryPath(repo with { Id = "repository:owner/a-repo" }));
        Assert.Equal(Launcher.EntryPath(repo), Launcher.EntryPath(repo with { FileName = "release-v3.zip" }));
        LocalApp local = new() { Id = "first", FileName = "My App v2.zip", InstallationName = "local/example" };
        Assert.Matches(@"^my-app-v2-[a-f0-9]{12}\.desktop$", Path.GetFileName(Launcher.EntryPath(local)));
        Assert.NotEqual(Launcher.EntryPath(local), Launcher.EntryPath(local with { Id = "second" }));
        foreach (string name in new[] { "../../unsafe name.zip", "日本語.zip", new string('a', 400) + ".zip" })
        {
            string path = Launcher.EntryPath(local with { FileName = name });
            Assert.Equal(Path.Combine(_root, "applications"), Path.GetDirectoryName(path));
            Assert.True(Path.GetFileName(path).Length < 150);
            Assert.Matches(@"^[a-z0-9._-]+-[a-f0-9]{12}\.desktop$", Path.GetFileName(path));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ReinstallMigratesLegacyFilenamesAndPreservesEditedLegacyLaunchers(bool changed, bool prefixed)
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(await Install(Archive(("app", "#!/bin/sh\n"))));
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        string readable = Launcher.EntryPath(app);
        string legacy = prefixed ? Launcher.PrefixedEntryPath(app) : Launcher.LegacyEntryPath(app);
        File.Move(readable, legacy);
        if (changed) await File.AppendAllTextAsync(legacy, "# user customization\n");
        string original = await File.ReadAllTextAsync(legacy);
        Assert.True(await Install(app.InstallerPath));
        LocalApp updated = Assert.Single(LocalApps.Store.Read());
        Assert.True(File.Exists(readable));
        Assert.Equal(LocalAppManager.FileHash(readable), updated.DesktopEntryHash);
        Assert.Equal(changed, File.Exists(legacy));
        if (changed) Assert.Equal(original, await File.ReadAllTextAsync(legacy));
        else Assert.Single(Directory.GetFiles(Path.Combine(_root, "applications"), "*.desktop"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UninstallCanRemoveALegacyLauncherBeforeItHasBeenMigrated(bool prefixed)
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(await Install(Archive(("app", "#!/bin/sh\n"))));
        LocalApp app = Assert.Single(LocalApps.Store.Read());
        string legacy = prefixed ? Launcher.PrefixedEntryPath(app) : Launcher.LegacyEntryPath(app);
        File.Move(Launcher.EntryPath(app), legacy);
        Launcher.Remove(app);
        Assert.False(File.Exists(legacy));
    }

    [Fact]
    public async Task RenamedArchiveVersionReplacesTheOldReadableLauncher()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(await Install(Archive(("app", "#!/bin/sh\n"))));
        LocalApp first = Assert.Single(LocalApps.Store.Read());
        string original = Launcher.EntryPath(first);
        Assert.True(await Install(Archive(("app", "#!/bin/sh\n# new version\n"))));
        LocalApp second = Assert.Single(LocalApps.Store.Read());
        Assert.Equal(first.Id, second.Id);
        string updated = Launcher.EntryPath(second);
        Assert.NotEqual(original, updated);
        Assert.False(File.Exists(original));
        Assert.True(File.Exists(updated));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "applications"), "*.desktop"));
        Launcher.Remove(second);
        Assert.False(File.Exists(updated));
    }

    [Theory]
    [InlineData("/tmp/my app", "\"/tmp/my app\"")]
    [InlineData("/tmp/100% app", "\"/tmp/100%% app\"")]
    [InlineData("/tmp/$app", "\"/tmp/\\\\$app\"")]
    [InlineData("/tmp/a\"b", "\"/tmp/a\\\\\"b\"")]
    public void DesktopExecQuotesPathsWithoutShellExpansion(string path, string expected) =>
        Assert.Equal(expected, ArchiveAppLauncher.QuoteExecutable(path));

    [Fact]
    public void ExecutableDiscoveryFindsNativeProgramsAndSkipsSharedLibraries()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.True(ArchiveAppLauncher.IsExecutable("/bin/sh"));
        string library = Path.Combine(_root, "library.so");
        File.Copy("/bin/sh", library);
        Assert.False(ArchiveAppLauncher.IsExecutable(library));
    }

    public void Dispose()
    {
        LocalApps.Store = _previousApps;
        OperationHistory.Store = _previousHistory;
        Directory.Delete(_root, recursive: true);
    }
}
