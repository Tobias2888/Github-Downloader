using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class ArchiveExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ghd-test-{Guid.NewGuid():N}");
    private const string Payload = "release content";

    public ArchiveExtractorTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingDownloadAssetIsReportedAsFailure(bool bulk)
    {
        Repo repo = CreateRepo();
        LoggerLib.Logger.LogDir = Path.Combine(_root, "logs");
        LoggerLib.Logger.LogToTerminal = false;
        await LoggerLib.Logger.CreateFile();

        bool succeeded = bulk
            ? await UpdateManager.UpdateReposAsync(new[] { repo }, _ => { }, _ => { })
            : await UpdateManager.UpdateRepo(repo, _ => { }, _ => { });

        Assert.False(succeeded);
        Assert.Equal("v1", repo.CurrentInstallTag);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadPipelineHonorsToggleAndDoesNotKeepExtractedArchives(bool autoExtract)
    {
        Repo repo = CreateRepo();
        repo.AutoExtract = autoExtract;
        repo.SaveFileAnyway = true;
        repo.NewFileName = "renamed.zip";
        string archive = CreateArchive("zip");
        byte[] original = File.ReadAllBytes(archive);
        Directory.CreateDirectory(repo.DownloadPath);
        LoggerLib.Logger.LogDir = Path.Combine(_root, "logs");
        LoggerLib.Logger.LogToTerminal = false;
        await LoggerLib.Logger.CreateFile();

        bool success = await UpdateManager.UpdateReposAsync(
            [new UpdateManager.Asset(repo, archive, "v2")], _ => { }, _ => { });

        Assert.True(success);
        Assert.Equal("v2", repo.CurrentInstallTag);
        string savedArchive = Path.Combine(repo.DownloadPath, "renamed.zip");
        if (autoExtract)
        {
            Assert.False(File.Exists(savedArchive));
            Assert.False(File.Exists(archive));
            Assert.Equal(Payload, File.ReadAllText(Path.Combine(repo.ExtractionDirectory, "nested", "file.txt")));
        }
        else
        {
            Assert.Equal(original, File.ReadAllBytes(savedArchive));
            Assert.False(Directory.Exists(repo.ExtractionDirectory));
        }
    }

    [Fact]
    public async Task DownloadPipelineReportsFailedExtractionAndCleansTemporaryFolder()
    {
        Repo repo = CreateRepo();
        string cache = Path.Combine(_root, "temporary-download");
        Directory.CreateDirectory(cache);
        string archive = Path.Combine(cache, "broken.zip");
        File.WriteAllText(archive, "not an archive");
        List<string> messages = new();
        LoggerLib.Logger.LogDir = Path.Combine(_root, "logs");
        LoggerLib.Logger.LogToTerminal = false;
        await LoggerLib.Logger.CreateFile();

        bool success = await UpdateManager.UpdateReposAsync(
            [new UpdateManager.Asset(repo, archive, "v2", cache)], _ => { }, messages.Add);

        Assert.False(success);
        Assert.Equal("v1", repo.CurrentInstallTag);
        Assert.False(Directory.Exists(cache));
        Assert.Contains(messages, message => message.Contains("Extraction failed"));
        AssertNoStagingDirectories();
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("tar")]
    [InlineData("tar.gz")]
    [InlineData("tgz")]
    [InlineData("7z")]
    public async Task ExtractsNestedFilesAndDeletesDownloadedArchive(string format)
    {
        string archive = CreateArchive(format);
        Repo repo = CreateRepo();
        List<string> progress = new();

        await ArchiveExtractor.ExtractDownloadAsync(repo, archive, "v2", progress.Add);

        Assert.Equal(Payload, File.ReadAllText(Path.Combine(repo.ExtractionDirectory, "nested", "file.txt")));
        Assert.Equal("v2", repo.CurrentInstallTag);
        Assert.False(File.Exists(archive));
        Assert.False(File.Exists(Path.Combine(repo.DownloadPath, Path.GetFileName(archive))));
        Assert.NotEmpty(progress);
        AssertNoStagingDirectories();
    }

    [Fact]
    public async Task UsesDownloadLocationAndPreservesUnrelatedFilesOnUpdate()
    {
        Repo repo = CreateRepo();
        repo.DownloadPath = Path.Combine(_root, "custom-downloads");
        Directory.CreateDirectory(Path.Combine(repo.ExtractionDirectory, "nested"));
        File.WriteAllText(Path.Combine(repo.ExtractionDirectory, "settings.json"), "user settings");
        File.WriteAllText(Path.Combine(repo.ExtractionDirectory, "nested", "file.txt"), "old release");

        await ArchiveExtractor.ExtractDownloadAsync(repo, CreateArchive("zip"), "v2", _ => { });

        Assert.Equal(Payload, File.ReadAllText(Path.Combine(repo.ExtractionDirectory, "nested", "file.txt")));
        Assert.Equal("user settings", File.ReadAllText(Path.Combine(repo.ExtractionDirectory, "settings.json")));
        AssertNoStagingDirectories();
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("nested/../../escape.txt")]
    [InlineData("..\\escape.txt")]
    [InlineData("/escape.txt")]
    [InlineData("C:\\escape.txt")]
    public async Task RejectsUnsafePathsBeforeChangingExistingFiles(string unsafeKey)
    {
        Repo repo = CreateRepo();
        Directory.CreateDirectory(repo.ExtractionDirectory);
        string existing = Path.Combine(repo.ExtractionDirectory, "file.txt");
        File.WriteAllText(existing, "old release");
        string archive = CreateZip(("file.txt", "new release"), (unsafeKey, Payload));

        await Assert.ThrowsAnyAsync<IOException>(() =>
            ArchiveExtractor.ExtractDownloadAsync(repo, archive, "v2", _ => { }));

        Assert.Equal("v1", repo.CurrentInstallTag);
        Assert.Equal("old release", File.ReadAllText(existing));
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
        Assert.False(File.Exists(archive));
        AssertNoStagingDirectories();
    }

    [Fact]
    public async Task CorruptArchiveLeavesVersionAndExistingFilesUnchanged()
    {
        Repo repo = CreateRepo();
        Directory.CreateDirectory(repo.ExtractionDirectory);
        string existing = Path.Combine(repo.ExtractionDirectory, "file.txt");
        File.WriteAllText(existing, "old release");
        string archive = Path.Combine(_root, "broken.zip");
        File.WriteAllText(archive, "not an archive");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            ArchiveExtractor.ExtractDownloadAsync(repo, archive, "v2", _ => { }));

        Assert.Equal("v1", repo.CurrentInstallTag);
        Assert.Equal("old release", File.ReadAllText(existing));
        Assert.False(File.Exists(archive));
        AssertNoStagingDirectories();
    }

    [Fact]
    public async Task RejectsArchiveSymlinks()
    {
        Repo repo = CreateRepo();
        string archive = Path.Combine(_root, "links.tar");
        using (FileStream stream = File.Create(archive))
        using (TarWriter writer = new(stream))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "../outside" });
        }

        await Assert.ThrowsAnyAsync<IOException>(() =>
            ArchiveExtractor.ExtractDownloadAsync(repo, archive, "v2", _ => { }));
        Assert.Equal("v1", repo.CurrentInstallTag);
        Assert.False(File.Exists(archive));
        AssertNoStagingDirectories();
    }

    [Fact]
    public async Task RejectsExistingDestinationSymlinks()
    {
        if (OperatingSystem.IsWindows()) return; // Windows symlink creation may require elevation.
        Repo repo = CreateRepo();
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(repo.ExtractionDirectory);
        Directory.CreateSymbolicLink(Path.Combine(repo.ExtractionDirectory, "nested"), outside);

        await Assert.ThrowsAnyAsync<IOException>(() =>
            ArchiveExtractor.ExtractDownloadAsync(repo, CreateArchive("zip"), "v2", _ => { }));
        Assert.Empty(Directory.GetFiles(outside));
        Assert.Equal("v1", repo.CurrentInstallTag);
        AssertNoStagingDirectories();
    }

    [Fact]
    public async Task PreservesTarExecutablePermission()
    {
        if (OperatingSystem.IsWindows()) return;
        Repo repo = CreateRepo();
        await ArchiveExtractor.ExtractDownloadAsync(repo, CreateArchive("tar"), "v2", _ => { });

        UnixFileMode mode = File.GetUnixFileMode(Path.Combine(repo.ExtractionDirectory, "nested", "file.txt"));
        Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public void SettingsRoundTripAndOldConfigurationsDefaultToDisabled()
    {
        Repo oldRepo = JsonSerializer.Deserialize<Repo>("{\"Url\":\"https://example.com\",\"Name\":\"owner/repo\"}")!;
        Assert.False(oldRepo.AutoExtract);
        Repo repo = CreateRepo();
        repo.DownloadPath = Path.Combine(_root, "custom-downloads");
        Repo restored = JsonSerializer.Deserialize<Repo>(JsonSerializer.Serialize(repo))!;
        Assert.True(restored.AutoExtract);
        Assert.Equal(repo.DownloadPath, restored.DownloadPath);
        Assert.Equal(Path.Combine(restored.DownloadPath, "owner-repo"), restored.ExtractionDirectory);
        repo.DownloadPath = Path.Combine(_root, "other-downloads");
        Assert.Equal(Path.Combine(repo.DownloadPath, "owner-repo"), repo.ExtractionDirectory);
    }

    [Theory]
    [InlineData("release.ZIP", true)]
    [InlineData("release.tar.gz", true)]
    [InlineData("release.tar.bz2", true)]
    [InlineData("release.tar.xz", true)]
    [InlineData("release.7z", true)]
    [InlineData("release.rar", true)]
    [InlineData("release.exe", false)]
    [InlineData("release.deb", false)]
    [InlineData("release.AppImage", false)]
    public void DetectsArchivesWithoutInterceptingInstallers(string name, bool expected) =>
        Assert.Equal(expected, ArchiveExtractor.IsSupportedArchive(name));

    private Repo CreateRepo() => new()
    {
        Url = "https://example.com", Name = "owner/repo", AutoExtract = true,
        DownloadPath = Path.Combine(_root, "downloads"), CurrentInstallTag = "v1", Tag = "v2"
    };

    private string CreateArchive(string format)
    {
        if (format == "zip") return CreateZip(("nested/file.txt", Payload));
        string path = Path.Combine(_root, $"release.{format}");
        using FileStream output = File.Create(path);
        if (format == "7z")
        {
            using IWriter writer = WriterFactory.OpenWriter(output, ArchiveType.SevenZip,
                new SevenZipWriterOptions(CompressionType.LZMA2));
            using MemoryStream content = new(Encoding.UTF8.GetBytes(Payload));
            writer.Write("nested/file.txt", content, DateTime.UtcNow);
        }
        else
        {
            using Stream tar = format == "tar" ? output : new GZipStream(output, CompressionLevel.Optimal);
            using TarWriter writer = new(tar);
            using MemoryStream content = new(Encoding.UTF8.GetBytes(Payload));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "nested/file.txt")
            {
                DataStream = content,
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            });
        }
        return path;
    }

    private string CreateZip(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(_root, $"release-{Guid.NewGuid():N}.zip");
        using FileStream stream = File.Create(path);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        foreach ((string Name, string Content) entry in entries)
        {
            using StreamWriter writer = new(archive.CreateEntry(entry.Name).Open());
            writer.Write(entry.Content);
        }
        return path;
    }

    private void AssertNoStagingDirectories() =>
        Assert.Empty(Directory.GetDirectories(_root, ".github-downloader-extract-*", SearchOption.AllDirectories));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
