using System.IO.Compression;
using Github_Downloader_cli.Args;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class LocalFileInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghd-local-test-" + Guid.NewGuid().ToString("N"));
    private readonly HistoryStore _previous = OperationHistory.Store;
    private readonly LocalAppStore _previousApps = LocalApps.Store;
    private string Cache => Path.Combine(_root, "cache");
    private string HistoryPath => Path.Combine(_root, "history.json");

    public LocalFileInstallerTests()
    {
        Directory.CreateDirectory(_root);
        OperationHistory.Store = new(HistoryPath);
        LocalApps.Store = new(Path.Combine(_root, "local-apps.json"));
    }

    [Theory]
    [InlineData(".deb", true)]
    [InlineData(".AppImage", true)]
    [InlineData(".EXE", true)]
    [InlineData(".msi", false)]
    public async Task LocalInstallersUseStagedCopiesAndRecordTheActualInstallerResult(string extension, bool success)
    {
        if (extension == ".deb" && !InstallationManager.CanInstallDebianPackages) return;
        if (extension == ".AppImage" && !OperatingSystem.IsLinux()) return;
        string source = Path.Combine(_root, "my installer 'quoted'" + extension);
        await File.WriteAllTextAsync(source, "original installer");
        Repo? installed = null;
        string? staged = null;
        bool result = await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null, async asset =>
        {
            installed = asset.Repo;
            staged = asset.TempAssetPath;
            Assert.NotEqual(source, staged);
            Assert.Equal("original installer", await File.ReadAllTextAsync(staged));
            // AppImage integration moves the supplied file; the user's original must survive.
            File.Delete(staged);
            return success;
        }, Cache);
        Assert.Equal(success, result);
        if (success)
        {
            LocalApp app = Assert.Single(new LocalAppStore(Path.Combine(_root, "local-apps.json")).Read());
            Assert.Equal("original installer", await File.ReadAllTextAsync(app.InstallerPath));
            Assert.StartsWith(Path.Combine(_root, "local-installers") + Path.DirectorySeparatorChar, app.InstallerPath);
            Assert.Equal(Path.GetFileNameWithoutExtension(source), Path.GetFileName(Path.GetDirectoryName(app.InstallerPath)));
            Assert.Equal(Path.GetFileName(source), Path.GetFileName(app.InstallerPath));
            File.Delete(source);
            Assert.True(app.CanReinstall);
            Assert.True(await LocalFileInstaller.InstallAsync(app.InstallerPath, _ => { }, _ => { }, null, async asset =>
            {
                Assert.Equal("original installer", await File.ReadAllTextAsync(asset.TempAssetPath));
                File.Delete(asset.TempAssetPath);
                return true;
            }, Cache));
            Assert.Equal(app.InstallerPath, Assert.Single(LocalApps.Store.Read()).InstallerPath);
            await File.WriteAllTextAsync(source, "original installer");
        }
        else
        {
            Assert.Empty(LocalApps.Store.Read());
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "local-installers"), "*", SearchOption.AllDirectories));
        }
        Assert.NotNull(installed);
        Assert.StartsWith("local/", installed.Name);
        Assert.Equal("original installer", await File.ReadAllTextAsync(source));
        Assert.False(File.Exists(staged));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Cache));
        HistoryEntry entry = new HistoryStore(HistoryPath).Query().Entries.Last();
        Assert.Equal(HistoryAction.Install, entry.Action);
        Assert.Equal(success ? HistoryStatus.Succeeded : HistoryStatus.Failed, entry.Status);
        Assert.Equal(success ? HistoryOutcome.Installed : HistoryOutcome.InstallFailed, entry.Outcome);
        Assert.Equal(Path.GetFileName(source), entry.LocalFileName);
        Assert.Equal("Local: " + Path.GetFileName(source), entry.DisplayName);
        Assert.Empty(entry.RepositoryKey);
        Assert.Empty(entry.RepositoryName);
        Assert.DoesNotContain(_root, File.ReadAllText(HistoryPath));
        Assert.Equal(success ? 2 : 1, OperationHistory.Store.Query(new(Repository: "quoted")).Entries.Count);
    }

    [Fact]
    public async Task SameNamedInstallersKeepSeparateReadableCopies()
    {
        string source = Path.Combine(_root, "example.deb");
        if (!InstallationManager.CanInstallDebianPackages) source = Path.Combine(_root, "example.exe");
        await File.WriteAllTextAsync(source, "first version");
        Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null,
            _ => Task.FromResult(true), Cache));
        string firstCopy = Assert.Single(LocalApps.Store.Read()).InstallerPath;
        await File.WriteAllTextAsync(source, "second version");
        Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null,
            _ => Task.FromResult(true), Cache));
        string secondCopy = Assert.Single(LocalApps.Store.Read()).InstallerPath;
        Assert.Equal("example", Path.GetFileName(Path.GetDirectoryName(firstCopy)));
        Assert.Equal("example-2", Path.GetFileName(Path.GetDirectoryName(secondCopy)));
        Assert.Equal("first version", await File.ReadAllTextAsync(firstCopy));
        Assert.Equal("second version", await File.ReadAllTextAsync(secondCopy));
    }

    [Fact]
    public async Task LocalArchivesExtractToTheChosenFolderAndPreserveTheSource()
    {
        string source = CreateArchive("archive.zip", "bin/application.txt");
        byte[] original = await File.ReadAllBytesAsync(source);
        string destination = Path.Combine(_root, "chosen folder");
        Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, destination, null, Cache));
        Assert.Equal("archive content", await File.ReadAllTextAsync(Path.Combine(destination, "bin", "application.txt")));
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Cache));
        HistoryEntry entry = Assert.Single(OperationHistory.Store.Query().Entries);
        Assert.Equal(HistoryAction.Extract, entry.Action);
        Assert.Equal(HistoryOutcome.Extracted, entry.Outcome);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("archive.zip")]
    public async Task InvalidArchiveDestinationsFailWithoutOverwritingTheOriginal(string entryName)
    {
        string source = CreateArchive("archive.zip", entryName);
        byte[] original = await File.ReadAllBytesAsync(source);
        Assert.False(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, _root, null, Cache));
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        Assert.False(File.Exists(Path.Combine(_root, "outside.txt")));
        HistoryEntry entry = Assert.Single(OperationHistory.Store.Query().Entries);
        Assert.Equal(HistoryStatus.Failed, entry.Status);
        Assert.Equal(HistoryOutcome.ExtractionFailed, entry.Outcome);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Cache));
    }

    [Fact]
    public async Task LinuxArchivesUseManagedAppFoldersAndReuseTheirInstallation()
    {
        if (!OperatingSystem.IsLinux()) return;
        string source = CreateArchive("managed.zip", "file.txt");
        Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null, null, Cache));
        LocalApp first = Assert.Single(LocalApps.Store.Read());
        Assert.StartsWith(LocalApps.Store.AppsPath + Path.DirectorySeparatorChar, first.ExtractionDirectory);
        Assert.Equal("archive content", await File.ReadAllTextAsync(Path.Combine(first.ExtractionDirectory, "file.txt")));
        Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null, null, Cache));
        LocalApp reinstalled = Assert.Single(LocalApps.Store.Read());
        Assert.Equal(first.ExtractionDirectory, reinstalled.ExtractionDirectory);
        Assert.Equal(first.Id, reinstalled.Id);
        Assert.True(File.Exists(source));
        string otherSource = CreateArchive("other.zip", "file.txt");
        Assert.True(await LocalFileInstaller.InstallAsync(otherSource, _ => { }, _ => { }, null, null, Cache));
        Assert.Equal(2, LocalApps.Store.Read().Count);
        Assert.Equal(2, LocalApps.Store.Read().Select(app => app.ExtractionDirectory).Distinct().Count());
        LocalApps.Store.Remove(first.Id);
        string unmanaged = Path.Combine(first.ExtractionDirectory, "file.txt");
        await File.WriteAllTextAsync(unmanaged, "keep unmanaged files");
        Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null, null, Cache));
        LocalApp fresh = Assert.Single(LocalApps.Store.Read(), app => app.FileName == first.FileName);
        Assert.NotEqual(first.ExtractionDirectory, fresh.ExtractionDirectory);
        Assert.Equal("keep unmanaged files", await File.ReadAllTextAsync(unmanaged));
    }

    [Fact]
    public async Task UnsupportedFilesAndMissingSourcesAreRejectedBeforeInstallation()
    {
        string source = Path.Combine(_root, "file.txt");
        await File.WriteAllTextAsync(source, "keep");
        await Assert.ThrowsAsync<ArgumentException>(() => LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null, null, Cache));
        if (!OperatingSystem.IsLinux())
        {
            string archive = CreateArchive("archive.zip", "file.txt");
            await Assert.ThrowsAsync<ArgumentException>(() => LocalFileInstaller.InstallAsync(archive, _ => { }, _ => { }, null, null, Cache));
        }
        await Assert.ThrowsAsync<FileNotFoundException>(() => LocalFileInstaller.InstallAsync(Path.Combine(_root, "missing.exe"), _ => { }, _ => { }, null, null, Cache));
        Assert.Empty(OperationHistory.Store.Query().Entries);
        Assert.False(Directory.Exists(Cache));
    }

    [Fact]
    public async Task InstallerExceptionsAreRecordedAsFailuresAndStagedFilesAreCleaned()
    {
        string source = Path.Combine(_root, "installer.exe");
        await File.WriteAllTextAsync(source, "keep");
        Assert.False(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, null,
            _ => throw new IOException("private installer output"), Cache));
        Assert.Equal("keep", await File.ReadAllTextAsync(source));
        Assert.Equal(HistoryOutcome.InstallFailed, Assert.Single(OperationHistory.Store.Query().Entries).Outcome);
        Assert.DoesNotContain("private installer output", File.ReadAllText(HistoryPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Cache));
    }

    [Fact]
    public async Task CliLocalInstallParsesPathsAndReportsFailureWithoutClaimingSuccess()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        Assert.Equal(2, await ArgInstallLocal.ExecuteAsync(["install-local"], output, error));
        Assert.Equal(2, await ArgInstallLocal.ExecuteAsync(["install-local", "file.zip", "--wrong", "folder"], output, error));
        Assert.Equal(1, await ArgInstallLocal.ExecuteAsync(["install-local", "my archive.zip", "--extract-to", "chosen folder"], output, error,
            (file, status, progress, directory) =>
            {
                Assert.Equal("my archive.zip", file);
                Assert.Equal("chosen folder", directory);
                return Task.FromResult(false);
            }));
        Assert.Contains("installation failed", output.ToString());
        Assert.DoesNotContain("Local file installed.", output.ToString());
    }

    private string CreateArchive(string filename, string entryName)
    {
        string path = Path.Combine(_root, filename);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using StreamWriter writer = new(archive.CreateEntry(entryName).Open());
        writer.Write("archive content");
        return path;
    }

    public void Dispose()
    {
        OperationHistory.Store = _previous;
        LocalApps.Store = _previousApps;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
