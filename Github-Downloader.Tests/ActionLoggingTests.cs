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

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task PackageInstallationForwardsBothStreamsAndLogsDiagnostics(int exitCode)
    {
        if (!OperatingSystem.IsLinux()) return;
        ProcessStartInfo start = new("/bin/sh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("printf '%s\\n' \"$GHD_TEST_PRIVATE/stdout\"; "
            + "attempt=0; while [ ! -f \"$GHD_TEST_SIGNAL\" ]; do "
            + "attempt=$((attempt + 1)); [ \"$attempt\" -lt 1000 ] || exit 99; sleep 0.01; done; "
            + "printf '%s' \"$GHD_TEST_PRIVATE/stderr\" >&2; exit \"$GHD_TEST_EXIT\"");
        start.Environment["GHD_TEST_PRIVATE"] = PrivateData;
        start.Environment["GHD_TEST_EXIT"] = exitCode.ToString();
        string signal = Path.Combine(_root, "output-received");
        start.Environment["GHD_TEST_SIGNAL"] = signal;
        System.Collections.Concurrent.ConcurrentQueue<string> progress = new();

        // The child cannot exit until stdout reaches the callback, proving output is live.
        Assert.Equal(exitCode, await UpdateManager.RunPackageInstallCommandAsync(start, line =>
        {
            progress.Enqueue(line);
            if (line == PrivateData + "/stdout") File.WriteAllText(signal, "received");
        }));

        Assert.Contains(PrivateData + "/stdout", progress);
        Assert.Contains(PrivateData + "/stderr", progress);
        string log = await ReadLogAsync();
        Assert.Contains("Package manager: " + PrivateData + "/stdout", log);
        Assert.Contains("Package manager: " + PrivateData + "/stderr", log);
        Assert.Contains(log.Split('\n'), line => line.Contains("[Error]", StringComparison.Ordinal)
            && line.Contains(PrivateData + "/stderr", StringComparison.Ordinal));
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
