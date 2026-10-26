using System.IO.Compression;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class ConfigurationArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghd-config-test-" + Guid.NewGuid().ToString("N"));

    public ConfigurationArchiveTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData(".zip", InstallerExportMode.None, 0)]
    [InlineData(".zip", InstallerExportMode.Newest, 2)]
    [InlineData(".zip", InstallerExportMode.All, 4)]
    [InlineData(".tar.gz", InstallerExportMode.None, 0)]
    [InlineData(".tar.gz", InstallerExportMode.Newest, 2)]
    [InlineData(".tar.gz", InstallerExportMode.All, 4)]
    [InlineData(".tag.gz", InstallerExportMode.Newest, 2)]
    public void RoundTripRestoresConfigsAndSelectedInstallersAfterOriginalIsRemoved(string extension, InstallerExportMode mode, int expected)
    {
        string source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "repos.json"), "[]");
        File.WriteAllText(Path.Combine(source, "appsettings.json"), "{\"CheckForUpdatesInterval\":17}");
        File.WriteAllText(Path.Combine(source, "history.json"), "history is not configuration");
        LocalAppStore store = new(Path.Combine(source, "local-apps.json"));
        foreach (string id in new[] { "one", "two" })
        {
            List<LocalInstallerVersion> versions = [];
            for (int version = 1; version <= 2; version++)
            {
                string path = Path.Combine(source, "local-installers", id, $"version-{version}.bin");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, $"{id}-{version}");
                // Use recorded save dates, not filenames or filesystem modification times.
                versions.Add(new(path, DateTimeOffset.UtcNow.AddDays(version)));
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-version));
            }
            store.Save(new() { Id = id, FileName = id, InstallationName = id,
                Kind = LocalAppKind.Archive, IsInstalled = true, InstalledAt = DateTimeOffset.UtcNow,
                DebianPackage = "source-debian", ArchPackage = "source-arch", AppImageId = "source-appimage",
                ExtractionDirectory = Path.Combine(source, "apps", id),
                ExecutablePath = Path.Combine(source, "apps", id, "app"), DesktopEntryHash = "source-launcher",
                DesktopIconPath = Path.Combine(source, "local-icons", "icon.png"),
                OwnedFiles = new() { ["app"] = "source-file-hash" },
                InstallerPath = versions[1].Path, InstallerVersions = versions });
        }
        string archive = Path.Combine(_root, "backup" + extension);
        ConfigurationArchive.Export(archive, mode, source);
        Directory.Delete(source, true);
        string target = Path.Combine(_root, "target");
        ConfigurationArchive.Import(archive, target);
        Assert.Equal("[]", File.ReadAllText(Path.Combine(target, "repos.json")));
        Assert.Equal("{\"CheckForUpdatesInterval\":17}", File.ReadAllText(Path.Combine(target, "appsettings.json")));
        Assert.False(File.Exists(Path.Combine(target, "history.json")));
        string installers = Path.Combine(target, "local-installers");
        string[] files = Directory.Exists(installers) ? Directory.GetFiles(installers, "*", SearchOption.AllDirectories) : [];
        Assert.Equal(expected, files.Length);
        IReadOnlyList<LocalApp> restored = new LocalAppStore(Path.Combine(target, "local-apps.json")).Read();
        Assert.Equal(2, restored.Count);
        Assert.Equal(expected, restored.Sum(app => app.InstallerVersions.Count));
        foreach (LocalApp app in restored)
        {
            Assert.False(app.IsInstalled);
            Assert.Equal("Uninstalled", app.Status);
            Assert.False(app.CanUninstall);
            Assert.False(app.UseSystemUninstaller);
            Assert.Equal(default, app.InstalledAt);
            Assert.Empty(app.DebianPackage);
            Assert.Empty(app.ArchPackage);
            Assert.Empty(app.AppImageId);
            Assert.Empty(app.ExtractionDirectory);
            Assert.Empty(app.ExecutablePath);
            Assert.Empty(app.DesktopEntryHash);
            Assert.Empty(app.DesktopIconPath);
            Assert.Null(app.OwnedFiles);
            Assert.Equal(mode != InstallerExportMode.None, app.CanReinstall);
            if (mode != InstallerExportMode.None)
            {
                Assert.EndsWith("version-2.bin", app.InstallerPath);
                Assert.All(app.InstallerVersions, version => Assert.True(File.Exists(version.Path)));
            }
        }
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("local-installers/../../outside.json")]
    [InlineData("local-installers/..\\outside.json")]
    [InlineData("/absolute.json")]
    [InlineData("unexpected.json")]
    [InlineData("repos.json")]
    public void InvalidArchiveDoesNotOverwriteExistingConfiguration(string badName)
    {
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "repos.json"), "original");
        string archive = Path.Combine(_root, "bad.zip");
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            Add(zip, "repos.json", "[]");
            Add(zip, "appsettings.json", "{}");
            Add(zip, badName, "malicious");
        }
        Assert.Throws<InvalidDataException>(() => ConfigurationArchive.Import(archive, target));
        Assert.Equal("original", File.ReadAllText(Path.Combine(target, "repos.json")));
        Assert.False(File.Exists(Path.Combine(_root, "outside.json")));
    }

    [Theory]
    [InlineData("null", "{}")]
    [InlineData("[]", "[]")]
    [InlineData("broken", "{}")]
    public void InvalidJsonDoesNotOverwriteExistingConfiguration(string repos, string settings)
    {
        string archive = Path.Combine(_root, "bad.zip");
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            Add(zip, "repos.json", repos);
            Add(zip, "appsettings.json", settings);
        }
        string target = Path.Combine(_root, "target");
        Assert.ThrowsAny<Exception>(() => ConfigurationArchive.Import(archive, target));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void ExportCannotOverwriteSourceConfigOrInstaller()
    {
        string installer = Path.Combine(_root, "local-installers", "installer.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(installer)!);
        File.WriteAllText(installer, "original installer");
        Assert.Throws<IOException>(() => ConfigurationArchive.Export(installer, InstallerExportMode.All, _root));
        Assert.Equal("original installer", File.ReadAllText(installer));
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using StreamWriter writer = new(zip.CreateEntry(name).Open());
        writer.Write(content);
    }
}
