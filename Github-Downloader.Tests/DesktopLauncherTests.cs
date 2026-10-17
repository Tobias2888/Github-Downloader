using System.Diagnostics;
using Github_Downloader_cli;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class DesktopLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghd gui test " + Guid.NewGuid().ToString("N"));

    public DesktopLauncherTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void MissingInstallationReportsAnActionableError()
    {
        using StringWriter error = new();

        int result = DesktopLauncher.Launch([Path.Combine(_root, "missing-desktop")], error);

        Assert.Equal(1, result);
        Assert.Contains("install", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartInfoUsesTheDesktopDirectoryAndPassesGuiAsOneArgument()
    {
        string executablePath = Path.Combine(_root, "Github Downloader Desktop");

        ProcessStartInfo startInfo = DesktopLauncher.CreateStartInfo(executablePath);

        Assert.Equal(executablePath, startInfo.FileName);
        Assert.Equal(_root, startInfo.WorkingDirectory);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(new[] { "--gui" }, startInfo.ArgumentList);
        Assert.Empty(startInfo.Arguments);
    }

    [Fact]
    public void FailedProcessStartReportsTheDesktopExecutable()
    {
        if (!OperatingSystem.IsLinux()) return;
        string executablePath = CreateExecutable("broken-desktop", "#!/ghd-test-missing-interpreter\n");
        using StringWriter error = new();

        int result = DesktopLauncher.Launch([executablePath], error);

        Assert.Equal(1, result);
        Assert.Contains(Path.GetFileName(executablePath), error.ToString());
    }

    [Fact]
    public async Task LaunchStartsTheFirstExistingDesktopWithSpacesAndReturnsBeforeItExits()
    {
        if (!OperatingSystem.IsLinux()) return;
        string startedPath = Path.Combine(_root, "started");
        string releasePath = Path.Combine(_root, "release");
        string exitedPath = Path.Combine(_root, "exited");
        string alternatePath = Path.Combine(_root, "alternate-started");
        string temporaryOutputPath = Path.Combine(_root, "started.tmp");
        string executablePath = CreateExecutable("Github Downloader Desktop",
            "#!/bin/sh\n" +
            "printf '%s\\n' \"$#\" \"$1\" > " + ShellQuote(temporaryOutputPath) + "\n" +
            "pwd -P >> " + ShellQuote(temporaryOutputPath) + "\n" +
            "mv " + ShellQuote(temporaryOutputPath) + " " + ShellQuote(startedPath) + "\n" +
            "count=0\n" +
            "while [ ! -f " + ShellQuote(releasePath) + " ] && [ \"$count\" -lt 1000 ]; do\n" +
            "  count=$((count + 1))\n" +
            "  sleep 0.01\n" +
            "done\n" +
            ": > " + ShellQuote(exitedPath) + "\n");
        string alternateExecutablePath = CreateExecutable("alternate-desktop",
            "#!/bin/sh\n: > " + ShellQuote(alternatePath) + "\n");
        using StringWriter error = new();

        try
        {
            Task<int> launch = Task.Run(() => DesktopLauncher.Launch(
                [Path.Combine(_root, "missing-desktop"), executablePath, alternateExecutablePath], error));
            int result = await launch.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(0, result);
            await WaitForFileAsync(startedPath);
            Assert.Equal(new[] { "1", "--gui", _root }, await File.ReadAllLinesAsync(startedPath));
            Assert.False(File.Exists(exitedPath));
            Assert.False(File.Exists(alternatePath));
            Assert.Empty(error.ToString());
        }
        finally
        {
            await File.WriteAllTextAsync(releasePath, "release");
            if (File.Exists(startedPath)) await WaitForFileAsync(exitedPath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuiEntrypointLeavesCliStateAndActiveDownloadsUntouched(bool extraArgument)
    {
        if (!OperatingSystem.IsLinux()) return;
        string cliDirectory = Path.Combine(_root, "cli");
        string dataDirectory = Path.Combine(_root, "data");
        string configDirectory = Path.Combine(_root, "config");
        string cacheDirectory = Path.Combine(_root, "cache");
        string sharedCacheDirectory = Path.Combine(cacheDirectory, "github-downloader");
        string downloadPath = Path.Combine(sharedCacheDirectory, "in-progress-download");
        string launchedPath = Path.Combine(_root, "launched");
        Directory.CreateDirectory(cliDirectory);
        Directory.CreateDirectory(sharedCacheDirectory);
        await File.WriteAllTextAsync(downloadPath, "active desktop download");
        foreach (string sourcePath in Directory.GetFiles(AppContext.BaseDirectory))
        {
            string name = Path.GetFileName(sourcePath);
            if (!name.EndsWith(".dll", StringComparison.Ordinal) && !name.EndsWith(".json", StringComparison.Ordinal)) continue;
            string destinationPath = Path.Combine(cliDirectory, name);
            if (name.StartsWith("ghd.", StringComparison.Ordinal)) File.Copy(sourcePath, destinationPath);
            else File.CreateSymbolicLink(destinationPath, sourcePath);
        }
        CreateExecutable(Path.Combine("cli", "Github-Downloader"),
            "#!/bin/sh\nprintf '%s\\n' \"$#\" \"$1\" > " + ShellQuote(launchedPath) + "\n");
        ProcessStartInfo startInfo = new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _root
        };
        startInfo.ArgumentList.Add(Path.Combine(cliDirectory, "ghd.dll"));
        startInfo.ArgumentList.Add("--gui");
        if (extraArgument) startInfo.ArgumentList.Add("extra");
        startInfo.Environment["XDG_DATA_HOME"] = dataDirectory;
        startInfo.Environment["XDG_CONFIG_HOME"] = configDirectory;
        startInfo.Environment["XDG_CACHE_HOME"] = cacheDirectory;
        using Process cli = new() { StartInfo = startInfo };

        Assert.True(cli.Start());
        Task<string> output = cli.StandardOutput.ReadToEndAsync();
        Task<string> error = cli.StandardError.ReadToEndAsync();
        try
        {
            await cli.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(extraArgument ? 1 : 0, cli.ExitCode);
            Assert.Empty(await output);
            if (extraArgument)
            {
                Assert.Contains("Usage: ghd --gui", await error);
                Assert.False(File.Exists(launchedPath));
            }
            else
            {
                Assert.Empty(await error);
                await WaitForFileAsync(launchedPath);
                Assert.Equal(new[] { "1", "--gui" }, await File.ReadAllLinesAsync(launchedPath));
            }
            Assert.Equal("active desktop download", await File.ReadAllTextAsync(downloadPath));
            Assert.False(Directory.Exists(dataDirectory));
            Assert.False(Directory.Exists(configDirectory));
        }
        finally
        {
            if (!cli.HasExited)
            {
                cli.Kill(true);
                await cli.WaitForExitAsync();
            }
        }
    }

    private string CreateExecutable(string name, string contents)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, contents);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";

    private static async Task WaitForFileAsync(string path)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!File.Exists(path)) await Task.Delay(20, timeout.Token);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
