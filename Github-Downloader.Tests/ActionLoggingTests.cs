using System.Diagnostics;
using Github_Downloader.Services;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using LoggerLib;
using Xunit;

namespace Github_Downloader.Tests;

// LoggerLib owns a global stream; isolate log-capture tests from other collections.
[Collection("Repository dragging")]
public sealed class ActionLoggingTests : IAsyncLifetime
{
    private const string PrivateData = "private-user-marker";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghd-action-logs-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        Logger.LogDir = _root;
        Logger.LogToTerminal = false;
        Logger.LogFirstChance = false;
        await Logger.CreateFile();
    }

    [Fact]
    public async Task ApiFailuresUseLoggerWithoutWritingUrlsTokensOrConsoleDiagnostics()
    {
        TextWriter original = Console.Out;
        using StringWriter console = new();
        try
        {
            Console.SetOut(console);
            using HttpResponseMessage? response = await Api.GetRequest(PrivateData + "/relative-url", PrivateData);
            Assert.Null(response);
            Assert.Empty(console.ToString());
        }
        finally { Console.SetOut(original); }

        string log = await ReadLogAsync();
        Assert.Contains("GitHub API request failed (InvalidOperationException)", log);
        Assert.DoesNotContain(PrivateData, log);
        Assert.DoesNotContain("First chance:", log);
    }

    [Fact]
    public async Task UninstallLogsOutcomesAndDoesNotForwardPrivateCommandOutput()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = new()
        {
            Name = PrivateData + "/repository", Url = PrivateData, CurrentInstallTag = "v1", InstalledDebianPackage = "private-package"
        };
        List<string> progress = [];
        InstallationManager manager = new(_root, _root, (_, callback) =>
        {
            callback(Path.Combine(_root, PrivateData, "folder", "installer-output"));
            return Task.FromResult(new InstallationManager.CommandResult(126, PrivateData));
        });
        Assert.False(await manager.UninstallAsync(repo, _ => { }, progress.Add));
        Assert.DoesNotContain(progress, line => line.Contains(PrivateData, StringComparison.Ordinal));
        string log = await ReadLogAsync();
        Assert.Contains("Repository uninstall started", log);
        Assert.Contains("Debian package removal failed (exit code 126)", log);
        Assert.DoesNotContain("Repository uninstall completed", log);
        Assert.DoesNotContain(PrivateData, log);
        Assert.DoesNotContain("private-package", log);
        Assert.DoesNotContain(_root, log);
    }

    [Fact]
    public async Task UninstallExceptionsLogTheirTypeWithoutTheirMessageOrStackTrace()
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = new() { Name = PrivateData, Url = PrivateData, CurrentInstallTag = "v1", InstalledDebianPackage = "private-package" };
        InstallationManager manager = new(_root, _root, (_, _) =>
            throw new IOException(Path.Combine(_root, PrivateData, "private-file")));
        await Assert.ThrowsAsync<IOException>(() => manager.UninstallAsync(repo, _ => { }, _ => { }));
        string log = await ReadLogAsync();
        Assert.Contains("Repository uninstall failed (IOException)", log);
        Assert.DoesNotContain(PrivateData, log);
        Assert.DoesNotContain(_root, log);
    }

    [Fact]
    public async Task NativeNotificationFailuresLogExitCodesWithoutStderrOrNotificationText()
    {
        if (!OperatingSystem.IsLinux()) return;
        ProcessStartInfo start = new("/bin/sh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("printf '%s' \"$GHD_TEST_PRIVATE\" >&2; exit 7");
        start.Environment["GHD_TEST_PRIVATE"] = PrivateData + "/folder/" + _root;
        Assert.False(await DesktopNotificationSender.SendAsync(start, null));
        string log = await ReadLogAsync();
        Assert.DoesNotContain("[Debug]", log);
        Assert.Contains("Desktop notification failed (exit code 7)", log);
        Assert.DoesNotContain(PrivateData, log);
        Assert.DoesNotContain(_root, log);
    }

    private async Task<string> ReadLogAsync()
    {
        // Await a final write so all earlier LoggerLib writes have flushed before reading.
        await Logger.LogI("Action log check completed");
        return string.Join('\n', Directory.GetFiles(_root, "*.log").Select(File.ReadAllText));
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }
}
