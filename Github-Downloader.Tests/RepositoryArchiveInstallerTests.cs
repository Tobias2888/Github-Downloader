using System.IO.Compression;
using System.Text.Json;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class RepositoryArchiveInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghd-repo-archive-" + Guid.NewGuid().ToString("N"));
    private readonly LocalAppStore _previousApps = LocalApps.Store;
    private readonly HistoryStore _previousHistory = OperationHistory.Store;
    private string DesktopDirectory => Path.Combine(_root, "applications");
    private ArchiveAppLauncher Launcher => new(DesktopDirectory);

    public RepositoryArchiveInstallerTests()
    {
        Directory.CreateDirectory(_root);
        LocalApps.Store = new(Path.Combine(_root, "local-apps.json"));
        OperationHistory.Store = new(Path.Combine(_root, "history.json"));
    }

    private Repo CreateRepo() => new()
    {
        Url = "https://github.com/owner/example.app", Name = "owner/example.app",
        DownloadPath = Path.Combine(_root, "downloads"), AutoExtract = true, Tag = "v2", CurrentInstallTag = "v1"
    };

    private string Archive(string version, params (string Name, string Content)[] files)
    {
        string path = Path.Combine(_root, "example-" + version + ".zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in files)
        {
            using StreamWriter writer = new(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
        return path;
    }

    private Task<bool> Apply(Repo repo, string archive, string tag,
        Func<string, IReadOnlyList<string>, Task<string?>>? picker = null, bool downloadOnly = false) =>
        UpdateManager.ApplyAssetAsync(new(repo, archive, tag), _ => { }, _ => { }, downloadOnly, null, picker, Launcher);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToggleControlsExecutableSelectionAndLauncherWhileArchivesAreAlwaysDiscarded(bool createLauncher)
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = CreateRepo();
        repo.CreateArchiveLauncher = createLauncher;
        repo.SaveFileAnyway = true;
        string archive = Archive("v2", ("bin/app", "#!/bin/sh\n"), ("bin/helper", "#!/bin/sh\n"));
        int picks = 0;
        Assert.True(await Apply(repo, archive, "v2", (directory, candidates) =>
        {
            picks++;
            Assert.Equal(RepositoryArchiveInstaller.GetInstallationDirectory(repo), directory);
            Assert.Equal(2, candidates.Count);
            return Task.FromResult<string?>(Path.Combine(directory, "bin", "app"));
        }));
        Assert.Equal(createLauncher ? 1 : 0, picks);
        Assert.Equal("v2", repo.CurrentInstallTag);
        Assert.False(File.Exists(archive));
        Assert.False(File.Exists(Path.Combine(repo.DownloadPath, Path.GetFileName(archive))));
        string destination = createLauncher ? RepositoryArchiveInstaller.GetInstallationDirectory(repo) : repo.ExtractionDirectory;
        Assert.True(File.Exists(Path.Combine(destination, "bin", "app")));
        if (createLauncher)
        {
            Assert.Equal(Path.Combine(LocalApps.Store.AppsPath, "owner", "example.app"), destination);
            Assert.False(Directory.Exists(repo.ExtractionDirectory));
        }
        Assert.Empty(LocalApps.Store.Read());
        Assert.False(Directory.Exists(LocalApps.Store.InstallersPath));
        if (createLauncher)
        {
            LocalApp installation = Assert.IsType<LocalApp>(repo.ArchiveInstallation);
            Assert.True(repo.CanUninstall);
            Assert.Empty(installation.InstallerPath);
            Assert.Empty(installation.InstallerVersions);
            Assert.Equal(2, installation.OwnedFiles.Count);
            string entry = Launcher.EntryPath(installation);
            Assert.Contains("Name=example.app\n", File.ReadAllText(entry));
            Assert.Contains("Exec=\"" + installation.ExecutablePath + "\"", File.ReadAllText(entry));
            Assert.True((File.GetUnixFileMode(installation.ExecutablePath) & UnixFileMode.UserExecute) != 0);
        }
        else
        {
            Assert.Null(repo.ArchiveInstallation);
            Assert.False(Directory.Exists(DesktopDirectory));
        }
    }

    [Fact]
    public async Task UpdatesReusePersistedExecutableAndDesktopEntryWithRenamedArchives()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = CreateRepo();
        string archive = Archive("v2", ("bin/app", "#!/bin/sh\n"), ("helper", "#!/bin/sh\n"));
        Assert.True(await Apply(repo, archive, "v2", (directory, _) => Task.FromResult<string?>(Path.Combine(directory, "bin", "app"))));
        LocalApp previous = Assert.IsType<LocalApp>(repo.ArchiveInstallation);
        repo = JsonSerializer.Deserialize<Repo>(JsonSerializer.Serialize(repo))!;
        repo.DownloadPath = Path.Combine(_root, "different-downloads");
        archive = Archive("v3-renamed", ("bin/app", "#!/bin/sh\n# version 3\n"), ("helper", "#!/bin/sh\n"));
        Assert.True(await Apply(repo, archive, "v3", (_, _) => throw new Exception("The saved executable should be reused.")));
        LocalApp updated = Assert.IsType<LocalApp>(repo.ArchiveInstallation);
        Assert.Equal(previous.Id, updated.Id);
        Assert.StartsWith(LocalApps.Store.AppsPath + Path.DirectorySeparatorChar, updated.ExtractionDirectory);
        Assert.Equal(previous.ExtractionDirectory, updated.ExtractionDirectory);
        Assert.Equal(previous.ExecutablePath, updated.ExecutablePath);
        Assert.False(Directory.Exists(repo.DownloadPath));
        Assert.Single(Directory.GetFiles(DesktopDirectory, "*.desktop"));
        Assert.Equal(LocalAppManager.FileHash(Launcher.EntryPath(updated)), updated.DesktopEntryHash);
        Assert.Equal("v3", repo.CurrentInstallTag);
        Assert.False(File.Exists(archive));
    }

    [Fact]
    public async Task LegacyHashedInstallationMovesToOwnerAndRepoAndRetainsSettingsAndExecutableChoice()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = CreateRepo();
        Assert.True(await Apply(repo, Archive("v2", ("bin/app", "#!/bin/sh\n"), ("helper", "#!/bin/sh\n")), "v2",
            (directory, _) => Task.FromResult<string?>(Path.Combine(directory, "bin", "app"))));
        LocalApp original = repo.ArchiveInstallation!;
        string destination = original.ExtractionDirectory;
        string legacy = RepositoryArchiveInstaller.LegacyInstallationDirectory(repo);
        Directory.Move(destination, legacy);
        string legacyExecutable = Path.Combine(legacy, "bin", "app");
        LocalApp old = original with { ExtractionDirectory = legacy, ExecutablePath = legacyExecutable };
        string entry = Launcher.EntryPath(old);
        File.WriteAllText(entry, File.ReadAllText(entry).Replace(destination, legacy));
        repo.ArchiveInstallation = old with { DesktopEntryHash = LocalAppManager.FileHash(entry) };
        File.WriteAllText(Path.Combine(legacy, "user-settings.json"), "user settings");
        Assert.True(await Apply(repo, Archive("v3", ("bin/app", "#!/bin/sh\n# v3\n"), ("helper", "#!/bin/sh\n")), "v3",
            (_, _) => throw new Exception("Migration should retain the executable choice.")));
        LocalApp updated = repo.ArchiveInstallation!;
        Assert.Equal(Path.Combine(LocalApps.Store.AppsPath, "owner", "example.app"), updated.ExtractionDirectory);
        Assert.False(Directory.Exists(legacy));
        Assert.Equal("user settings", File.ReadAllText(Path.Combine(destination, "user-settings.json")));
        Assert.Equal(Path.Combine(destination, "bin", "app"), updated.ExecutablePath);
        Assert.Contains(updated.ExecutablePath, File.ReadAllText(entry));
        Assert.DoesNotContain(legacy, File.ReadAllText(entry));
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(2, updated.OwnedFiles.Count);
        Assert.False(File.Exists(Path.Combine(_root, "example-v3.zip")));
    }

    [Fact]
    public async Task MigrationDoesNotOverwriteAnExistingDestinationFolder()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = CreateRepo();
        string legacy = RepositoryArchiveInstaller.LegacyInstallationDirectory(repo);
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "old"), "old installation");
        repo.ArchiveInstallation = new() { Id = "repository:" + repo.Name, InstallationName = repo.Name,
            FileName = "app.zip", Kind = LocalAppKind.Archive, ExtractionDirectory = legacy };
        string destination = RepositoryArchiveInstaller.GetInstallationDirectory(repo);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "keep"), "unrelated files");
        string archive = Archive("v2", ("app", "#!/bin/sh\n"));
        Assert.False(await Apply(repo, archive, "v2"));
        Assert.Equal("old installation", File.ReadAllText(Path.Combine(legacy, "old")));
        Assert.Equal("unrelated files", File.ReadAllText(Path.Combine(destination, "keep")));
        Assert.False(File.Exists(archive));
        Assert.Equal("v1", repo.CurrentInstallTag);
    }

    [Theory]
    [InlineData("owner/..")]
    [InlineData("../app")]
    [InlineData("owner/app/escape")]
    [InlineData("/absolute")]
    public void RepositoryInstallPathsRejectUnsafeNames(string name)
    {
        Repo repo = CreateRepo();
        repo.Name = name;
        Assert.Throws<IOException>(() => RepositoryArchiveInstaller.GetInstallationDirectory(repo));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    public async Task RepositoryLaunchersMigrateToOwnerAndRepoFilenames(int format, bool changed)
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = CreateRepo();
        Assert.True(await Apply(repo, Archive("v2", ("app", "#!/bin/sh\n")), "v2"));
        LocalApp installed = repo.ArchiveInstallation!;
        string current = Launcher.EntryPath(installed);
        Assert.Equal("owner_example.app.desktop", Path.GetFileName(current));
        string old = format switch
        {
            0 => Launcher.LegacyEntryPath(installed),
            1 => Launcher.LegacyReadableEntryPath(installed),
            _ => Launcher.PrefixedEntryPath(installed)
        };
        File.Move(current, old);
        if (changed) File.AppendAllText(old, "# user edit\n");
        string oldContent = File.ReadAllText(old);
        Assert.True(await Apply(repo, Archive("v3", ("app", "#!/bin/sh\n# updated\n")), "v3"));
        Assert.True(File.Exists(current));
        Assert.Equal(changed, File.Exists(old));
        if (changed) Assert.Equal(oldContent, File.ReadAllText(old));
        else Assert.Single(Directory.GetFiles(DesktopDirectory, "*.desktop"));
        Launcher.Remove(repo.ArchiveInstallation!);
        Assert.False(File.Exists(current));
    }

    [Fact]
    public async Task FailedLauncherCreationKeepsFilesTrackedAndDeletesArchiveWithoutConfirmingVersion()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = CreateRepo();
        string archive = Archive("v2", ("data", "payload"));
        string outside = Path.Combine(_root, "outside");
        File.WriteAllText(outside, "outside");
        Assert.False(await Apply(repo, archive, "v2", (_, _) => Task.FromResult<string?>(outside)));
        Assert.False(File.Exists(archive));
        Assert.Equal("v1", repo.CurrentInstallTag);
        Assert.True(repo.CanUninstall);
        Assert.Contains("data", repo.ArchiveInstallation!.OwnedFiles.Keys);
        Assert.Equal("outside", File.ReadAllText(outside));
        Assert.False(Directory.Exists(DesktopDirectory));
    }

    [Fact]
    public async Task CancellingExecutableSelectionKeepsExtractionWithoutDesktopEntry()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = CreateRepo();
        string archive = Archive("v2", ("data", "payload"));
        Assert.True(await Apply(repo, archive, "v2", (_, _) => Task.FromResult<string?>(null)));
        Assert.False(File.Exists(archive));
        Assert.Empty(repo.ArchiveInstallation!.ExecutablePath);
        Assert.False(Directory.Exists(DesktopDirectory));
        Assert.True(File.Exists(Path.Combine(RepositoryArchiveInstaller.GetInstallationDirectory(repo), "data")));
    }

    [Fact]
    public async Task DownloadOnlyKeepsArchiveWithoutExtractingOrCreatingLauncher()
    {
        Repo repo = CreateRepo();
        repo.NewFileName = "saved.zip";
        Directory.CreateDirectory(repo.DownloadPath);
        string archive = Archive("v2", ("app", "#!/bin/sh\n"));
        Assert.True(await Apply(repo, archive, "v2", (_, _) => throw new Exception("No picker for downloads."), downloadOnly: true));
        Assert.Null(repo.ArchiveInstallation);
        Assert.False(Directory.Exists(repo.ExtractionDirectory));
        Assert.True(File.Exists(Path.Combine(repo.DownloadPath, "saved.zip")));
        Assert.Equal("v1", repo.CurrentInstallTag);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleAndBulkRepositoryUpdatesPassTheExecutablePicker(bool batch)
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = CreateRepo();
        repo.ReplaceAssets(["example-v2.zip"], ["https://example.invalid/archive"]);
        repo.DownloadAssetIndex = 0;
        string source = Archive("source", ("data", "payload"));
        int picks = 0;
        Task Download(string url, string path, IProgress<double> progress)
        {
            File.Copy(source, path);
            return Task.CompletedTask;
        }
        Task<string?> Pick(string directory, IReadOnlyList<string> candidates)
        {
            picks++;
            Assert.Equal(RepositoryArchiveInstaller.GetInstallationDirectory(repo), directory);
            Assert.True(File.Exists(Path.Combine(directory, "data")));
            return Task.FromResult<string?>(null);
        }
        string cache = Path.Combine(_root, "cache");
        if (batch)
            Assert.True(await UpdateManager.RunBatchAsync([repo], _ => { }, _ => { }, true, false,
                downloadFile: Download, cachePath: cache, selectArchiveExecutable: Pick));
        else
            Assert.Equal(HistoryStatus.Succeeded, await UpdateManager.RunRepositoryAsync(repo, _ => { }, _ => { }, true,
                downloadFile: Download, cachePath: cache, selectArchiveExecutable: Pick));
        Assert.Equal(1, picks);
        Assert.False(Directory.Exists(repo.DownloadPath) && Directory.EnumerateFiles(repo.DownloadPath, "*.zip", SearchOption.AllDirectories).Any());
        Assert.False(Directory.Exists(cache) && Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task UninstallRemovesTrackedFilesAndLauncherAndKeepsChangedOrUnrelatedFiles()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = CreateRepo();
        string archive = Archive("v2", ("app", "#!/bin/sh\n"), ("settings", "default"));
        Assert.True(await Apply(repo, archive, "v2"));
        LocalApp installed = repo.ArchiveInstallation!;
        File.WriteAllText(Path.Combine(installed.ExtractionDirectory, "settings"), "user settings");
        File.WriteAllText(Path.Combine(installed.ExtractionDirectory, "unrelated"), "keep");
        InstallationManager manager = new(_root, DesktopDirectory, (_, _) => throw new Exception("No package commands needed."));
        Assert.True(await manager.UninstallAsync(repo, _ => { }, _ => { }));
        Assert.False(File.Exists(installed.ExecutablePath));
        Assert.False(File.Exists(Launcher.EntryPath(installed)));
        Assert.Equal("user settings", File.ReadAllText(Path.Combine(installed.ExtractionDirectory, "settings")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(installed.ExtractionDirectory, "unrelated")));
        Assert.False(repo.CanUninstall);
        Assert.Empty(repo.CurrentInstallTag);
        Assert.True(repo.ExcludedFromDownloadAll);
        Assert.Empty(LocalApps.Store.Read());
    }

    [Theory]
    [InlineData(".zip")]
    [InlineData(".tar.gz")]
    public void LauncherSettingPersistsButImportedConfigsDoNotClaimAnArchiveIsInstalled(string extension)
    {
        Repo legacy = JsonSerializer.Deserialize<Repo>("{\"Url\":\"\",\"Name\":\"owner/app\"}")!;
        Assert.True(legacy.CreateArchiveLauncher);
        Repo repo = CreateRepo();
        repo.CreateArchiveLauncher = false;
        repo.ArchiveInstallation = new() { Id = "example", FileName = "app.zip", InstallationName = repo.Name };
        string source = Path.Combine(_root, "config");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "repos.json"), JsonSerializer.Serialize(new[] { repo }));
        File.WriteAllText(Path.Combine(source, "appsettings.json"), "{}");
        string archive = Path.Combine(_root, "backup" + extension);
        ConfigurationArchive.Export(archive, InstallerExportMode.None, source);
        string target = Path.Combine(_root, "restored");
        ConfigurationArchive.Import(archive, target);
        Repo restored = Assert.Single(JsonSerializer.Deserialize<List<Repo>>(File.ReadAllText(Path.Combine(target, "repos.json")))!);
        Assert.False(restored.CreateArchiveLauncher);
        Assert.Null(restored.ArchiveInstallation);
        Assert.Empty(restored.CurrentInstallTag);
    }

    public void Dispose()
    {
        LocalApps.Store = _previousApps;
        OperationHistory.Store = _previousHistory;
        Directory.Delete(_root, true);
    }
}
