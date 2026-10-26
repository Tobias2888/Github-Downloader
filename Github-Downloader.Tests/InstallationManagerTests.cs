using System.Diagnostics;
using System.Text.Json;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class InstallationManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghd-uninstall-" + Guid.NewGuid().ToString("N"));
    private string AppImages => Path.Combine(_root, "app-images");
    private string Desktops => Path.Combine(_root, "applications");

    public InstallationManagerTests()
    {
        Directory.CreateDirectory(AppImages);
        Directory.CreateDirectory(Desktops);
    }

    [Theory]
    [InlineData("Package: actual-package\nArchitecture: amd64\n", "actual-package:amd64")]
    [InlineData("Architecture: all\nPackage: different-name\n", "different-name")]
    public async Task RecordsActualPackageIdentityRatherThanReleaseFileName(string metadata, string expected)
    {
        List<ProcessStartInfo> commands = [];
        InstallationManager manager = CreateManager(commands, new(0, metadata));
        string path = Path.Combine(_root, "unrelated-release-name.deb");
        Assert.Equal(expected, await manager.ReadDebianPackageNameAsync(path));
        Assert.Equal("/usr/bin/dpkg-deb", commands.Single().FileName);
        Assert.Equal(new[] { "--field", path, "Package", "Architecture" }, commands.Single().ArgumentList);
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(0, "Package: --danger\nArchitecture: amd64")]
    [InlineData(0, "Package: package\nArchitecture: ")]
    [InlineData(0, "Package: package+\nArchitecture: ../../outside")]
    public async Task InvalidOrUnreadableMetadataFailsBeforeRecordingAnIdentity(int exit, string output)
    {
        InstallationManager manager = CreateManager([], new(exit, output));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadDebianPackageNameAsync("asset.deb"));
    }

    [Theory]
    [InlineData("valid-package", true)]
    [InlineData("valid-package:amd64", true)]
    [InlineData("libexample++", true)]
    [InlineData("", false)]
    [InlineData("--purge", false)]
    [InlineData("package; touch /tmp/never", false)]
    [InlineData("package other", false)]
    [InlineData("package*", false)]
    [InlineData("package\n", false)]
    [InlineData("package:all --purge", false)]
    public void PackageNamesCannotInjectArgumentsOrPackagePatterns(string name, bool expected) =>
        Assert.Equal(expected, InstallationManager.IsValidDebianPackageName(name));

    [Fact]
    public async Task DebianUninstallUsesRecordedIdentityEvenAfterTheAssetSelectionChanges()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = NewRepo();
        repo.InstalledDebianPackage = "real-package:amd64";
        repo.AssetNames = ["other.zip"];
        List<ProcessStartInfo> commands = [];
        InstallationManager manager = CreateManager(commands, new(0, "removed"));
        Assert.True(await manager.UninstallAsync(repo, _ => { }, _ => { }));
        ProcessStartInfo start = Assert.Single(commands);
        Assert.False(start.UseShellExecute);
        Assert.Equal(new[] { "/usr/bin/dpkg", "--remove", "--", "real-package:amd64" }, start.ArgumentList);
        Assert.Empty(repo.InstalledDebianPackage);
        Assert.Empty(repo.CurrentInstallTag);
        Assert.True(repo.ExcludedFromDownloadAll);
        Assert.False(repo.CanUninstall);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(126)]
    [InlineData(127)]
    public async Task FailedOrCancelledPackageRemovalKeepsInstallationState(int exit)
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = NewRepo();
        repo.InstalledDebianPackage = "real-package";
        InstallationManager manager = CreateManager([], new(exit, "failed"));
        Assert.False(await manager.UninstallAsync(repo, _ => { }, _ => { }));
        Assert.Equal("real-package", repo.InstalledDebianPackage);
        Assert.Equal("v1", repo.CurrentInstallTag);
        Assert.False(repo.ExcludedFromDownloadAll);
    }

    [Fact]
    public async Task OlderDebianInstallationsRequireAnExplicitPackageName()
    {
        if (!InstallationManager.CanInstallDebianPackages) return;
        Repo repo = NewRepo();
        repo.AssetNames = ["unknown-name.deb"];
        List<ProcessStartInfo> commands = [];
        InstallationManager manager = CreateManager(commands, new(0, ""));
        Assert.True(repo.NeedsDebianPackageName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UninstallAsync(repo, _ => { }, _ => { }));
        Assert.Empty(commands);
        Assert.True(await manager.UninstallAsync(repo, _ => { }, _ => { }, " actual-package "));
        Assert.Equal("actual-package", commands.Single().ArgumentList.Last());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppImageUninstallRemovesManagedFilesAndKeepsUnrelatedFilesAndDownloads(bool recorded)
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = NewRepo();
        if (recorded) repo.InstalledAppImageId = "owner-repo";
        repo.AssetNames = [recorded ? "changed.zip" : "release.APPIMAGE"];
        string directory = CreateAppImage("owner-repo");
        string unrelated = Path.Combine(directory, "notes.txt");
        string savedCopy = Path.Combine(_root, "saved.AppImage");
        File.WriteAllText(unrelated, "keep");
        File.WriteAllText(savedCopy, "saved download");
        List<ProcessStartInfo> commands = [];
        InstallationManager manager = CreateManager(commands, new(0, ""));
        Assert.True(await manager.UninstallAsync(repo, _ => { }, _ => { }));
        Assert.False(File.Exists(Path.Combine(directory, "owner-repo.AppImage")));
        Assert.False(File.Exists(Path.Combine(directory, "icon.png")));
        Assert.False(File.Exists(Path.Combine(Desktops, "owner-repo.desktop")));
        Assert.Equal("keep", File.ReadAllText(unrelated));
        Assert.True(File.Exists(savedCopy));
        Assert.Empty(repo.InstalledAppImageId);
        Assert.Empty(repo.CurrentInstallTag);
        Assert.True(repo.ExcludedFromDownloadAll);
        Assert.Empty(commands);
    }

    [Fact]
    public async Task EmptyAppImageInstallationDirectoryIsRemoved()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = NewRepo();
        repo.InstalledAppImageId = "owner-repo";
        string directory = CreateAppImage("owner-repo");
        InstallationManager manager = CreateManager([], new(0, ""));
        Assert.True(await manager.UninstallAsync(repo, _ => { }, _ => { }));
        Assert.False(Directory.Exists(directory));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..")]
    [InlineData("/tmp/outside")]
    [InlineData("bad\\path")]
    public async Task InvalidAppImagePathsDoNotDeleteFilesOrRunRemovalCommands(string id)
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = NewRepo();
        repo.InstalledAppImageId = id;
        repo.InstalledDebianPackage = "valid-package";
        List<ProcessStartInfo> commands = [];
        InstallationManager manager = CreateManager(commands, new(0, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UninstallAsync(repo, _ => { }, _ => { }));
        Assert.Equal("v1", repo.CurrentInstallTag);
        Assert.Empty(commands);
    }

    [Fact]
    public async Task SymlinkedAppImageDirectoriesAreNotFollowed()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = NewRepo();
        repo.InstalledAppImageId = "owner-repo";
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "icon.png"), "keep");
        Directory.CreateSymbolicLink(Path.Combine(AppImages, "owner-repo"), outside);
        InstallationManager manager = CreateManager([], new(0, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UninstallAsync(repo, _ => { }, _ => { }));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(outside, "icon.png")));
    }

    [Fact]
    public async Task UnrelatedLauncherIsNotDeleted()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = NewRepo();
        repo.InstalledAppImageId = "owner-repo";
        string directory = CreateAppImage("owner-repo");
        string launcher = Path.Combine(Desktops, "owner-repo.desktop");
        File.WriteAllText(launcher, "[Desktop Entry]\nExec=/another/application\n");
        InstallationManager manager = CreateManager([], new(0, ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UninstallAsync(repo, _ => { }, _ => { }));
        Assert.True(File.Exists(launcher));
        Assert.True(File.Exists(Path.Combine(directory, "owner-repo.AppImage")));
    }

    [Fact]
    public async Task FailedDebianRemovalKeepsASecondAppImageInstallation()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = NewRepo();
        repo.InstalledDebianPackage = "package-name";
        repo.InstalledAppImageId = "owner-repo";
        string directory = CreateAppImage("owner-repo");
        InstallationManager manager = CreateManager([], new(126, "authorization cancelled"));
        Assert.False(await manager.UninstallAsync(repo, _ => { }, _ => { }));
        Assert.True(File.Exists(Path.Combine(directory, "owner-repo.AppImage")));
        Assert.Equal("owner-repo", repo.InstalledAppImageId);
    }

    [Fact]
    public void InstallationMetadataRoundTripsAndNotifiesAvailabilityChanges()
    {
        Repo repo = NewRepo();
        List<string?> properties = [];
        repo.PropertyChanged += (_, args) => properties.Add(args.PropertyName);
        repo.InstalledDebianPackage = "package-name:amd64";
        repo.InstalledAppImageId = "owner-repo";
        repo.AssetNames = ["unrelated.zip"];
        Repo restored = JsonSerializer.Deserialize<Repo>(JsonSerializer.Serialize(repo))!;
        Assert.Equal(repo.InstalledDebianPackage, restored.InstalledDebianPackage);
        Assert.Equal(repo.InstalledAppImageId, restored.InstalledAppImageId);
        Assert.Equal(OperatingSystem.IsLinux(), restored.CanUninstall);
        Assert.Contains(nameof(Repo.CanUninstall), properties);
        Assert.False(restored.NeedsDebianPackageName);
    }

    [Fact]
    public void DownloadOnlyAndUninstalledReposDoNotOfferUninstall()
    {
        Repo repo = NewRepo();
        repo.AssetNames = ["archive.zip"];
        Assert.False(repo.CanUninstall);
        repo.AssetNames = ["installer.deb"];
        repo.CurrentInstallTag = "";
        Assert.False(repo.CanUninstall);
        Assert.False(repo.NeedsDebianPackageName);
    }

    private InstallationManager CreateManager(List<ProcessStartInfo> commands, InstallationManager.CommandResult result) =>
        new(AppImages, Desktops, (start, _) =>
        {
            commands.Add(start);
            return Task.FromResult(result);
        });

    private string CreateAppImage(string id)
    {
        string directory = Path.Combine(AppImages, id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, id + ".AppImage"), "executable");
        File.WriteAllText(Path.Combine(directory, "icon.png"), "icon");
        File.WriteAllText(Path.Combine(Desktops, id + ".desktop"), "[Desktop Entry]\nExec=" + Path.Combine(directory, id + ".AppImage") + "\n");
        return directory;
    }

    private static Repo NewRepo() => new()
    {
        Name = "owner/repo", Url = "https://github.com/owner/repo", CurrentInstallTag = "v1", Tag = "v2"
    };

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
