using GithubDownloader.Core;
using GithubDownloader.Core.Models;
using Xunit;

namespace GithubDownloader.Tests;

public sealed class DesktopEntryArgumentsTests
{
    [Theory]
    [InlineData("--flag 'two words' \"\"", "\"/app\" \"--flag\" \"two words\" \"\"")]
    [InlineData("100% %f", "\"/app\" \"100%%\" \"%%f\"")]
    [InlineData("", "\"/app\"")]
    public void ArgumentsAreQuotedAsLiteralValues(string input, string expected) =>
        Assert.Equal(expected, DesktopEntryCommand.Create("/app", input));

    [Theory]
    [InlineData("--flag\nExec=other")]
    [InlineData("\"unclosed")]
    [InlineData("trailing\\")]
    public void InvalidArgumentsAreRejected(string input) =>
        Assert.Throws<ArgumentException>(() => DesktopEntryCommand.Create("/app", input));

    [Fact]
    public async Task AppImageArgumentsCanBeChangedClearedAndUninstalled()
    {
        if (!OperatingSystem.IsLinux()) return;
        string root = Path.Combine(Path.GetTempPath(), "ghd-args-" + Guid.NewGuid().ToString("N"));
        try
        {
            string images = Path.Combine(root, "images");
            string desktop = Path.Combine(root, "desktop");
            string executable = Path.Combine(images, "owner-app", "owner-app.AppImage");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            Directory.CreateDirectory(desktop);
            File.WriteAllText(executable, "app");
            string entry = Path.Combine(desktop, "owner-app.desktop");
            File.WriteAllText(entry, "[Desktop Entry]\nExec=" + executable + "\n");
            Repo repo = new() { Url = "", Name = "owner/app", InstalledAppImageId = "owner-app" };
            InstallationManager manager = new(images, desktop, (_, _) => throw new Exception("No commands needed."));
            manager.SetAppImageArguments(repo, "--test");
            repo.DesktopEntryArguments = "--test";
            Assert.Contains("Exec=" + DesktopEntryCommand.Create(executable, "--test"), File.ReadAllLines(entry));
            string before = File.ReadAllText(entry);
            Assert.Throws<ArgumentException>(() => manager.SetAppImageArguments(repo, "bad\nExec=other"));
            Assert.Equal(before, File.ReadAllText(entry));
            File.WriteAllText(entry, "Exec=/another/app\n");
            Assert.Throws<InvalidOperationException>(() => manager.SetAppImageArguments(repo, "--other"));
            Assert.Equal("Exec=/another/app\n", File.ReadAllText(entry));
            File.WriteAllText(entry, before);
            manager.SetAppImageArguments(repo, "");
            repo.DesktopEntryArguments = "";
            manager.SetAppImageArguments(repo, "--again");
            repo.DesktopEntryArguments = "--again";
            Assert.True(await manager.UninstallAsync(repo, _ => { }, _ => { }));
            Assert.False(File.Exists(entry));
            Assert.False(File.Exists(executable));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
