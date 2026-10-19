using System.Diagnostics;
using System.Text.Json;
using Github_Downloader_cli.Args;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class OperationHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghd-history-test-" + Guid.NewGuid().ToString("N"));
    private readonly HistoryStore _previous = OperationHistory.Store;
    private string HistoryPath => Path.Combine(_root, "history.json");
    private HistoryStore Store => OperationHistory.Store;

    public OperationHistoryTests()
    {
        Directory.CreateDirectory(_root);
        OperationHistory.Store = new(HistoryPath);
    }

    [Fact]
    public void CompletedHistorySurvivesReopeningAndRepositoryRemoval()
    {
        Repo repo = NewRepo();
        using (HistoryScope operation = Store.Begin(HistoryAction.Install, repo)) operation.Complete(HistoryOutcome.Installed);
        UpdateManager.Repos = [repo];
        UpdateManager.Repos.Clear();
        HistoryEntry entry = Assert.Single(new HistoryStore(HistoryPath).Query().Entries);
        Assert.Equal("owner/repo", entry.RepositoryName);
        Assert.Equal("v1", entry.FromVersion);
        Assert.Equal("v2", entry.ToVersion);
        Assert.Equal(HistoryStatus.Succeeded, entry.Status);
        Assert.NotNull(entry.FinishedAt);
        Assert.Equal(HistoryOutcome.Installed, entry.Outcome);
    }

    [Fact]
    public void DeadProcessEntriesAreInterruptedWhileLiveEntriesRemainRunning()
    {
        using HistoryScope live = Store.Begin(HistoryAction.Download, NewRepo());
        Assert.Equal(HistoryStatus.Running, Assert.Single(new HistoryStore(HistoryPath).Query().Entries).Status);
        string json = File.ReadAllText(HistoryPath).Replace($"\"ProcessId\": {Environment.ProcessId}", "\"ProcessId\": 2147483647");
        File.WriteAllText(HistoryPath, json);
        HistoryEntry recovered = Assert.Single(new HistoryStore(HistoryPath).Query().Entries);
        Assert.Equal(HistoryStatus.Interrupted, recovered.Status);
        Assert.Contains("Interrupted", recovered.Summary);
        Assert.Equal(HistoryStatus.Interrupted, Assert.Single(new HistoryStore(HistoryPath).Query().Entries).Status);
    }

    [Fact]
    public void ClearingHistoryKeepsRunningEntriesAndDoesNotChangeRepositoryState()
    {
        Repo repo = NewRepo();
        using (HistoryScope completed = Store.Begin(HistoryAction.Download, repo)) completed.Complete(HistoryOutcome.Downloaded);
        using HistoryScope running = Store.Begin(HistoryAction.Install, repo);
        Assert.Equal(1, Store.Clear());
        HistoryEntry entry = Assert.Single(Store.Query().Entries);
        Assert.Equal(running.Id, entry.Id);
        Assert.Equal("v1", repo.CurrentInstallTag);
        running.Complete(HistoryOutcome.Installed);
        Assert.Equal(1, Store.Clear());
        Assert.Empty(Store.Query().Entries);
    }

    [Fact]
    public async Task ConcurrentWritersKeepAllEntries()
    {
        Task[] writers = Enumerable.Range(0, 12).Select(index => Task.Run(() =>
        {
            HistoryStore writer = new(HistoryPath);
            using HistoryScope operation = writer.Begin(HistoryAction.Download, NewRepo("owner/repo-" + index));
            operation.Complete(HistoryOutcome.Downloaded);
        })).ToArray();
        await Task.WhenAll(writers);
        Assert.Equal(12, Store.Query().Total);
        Assert.All(Store.Query().Entries, entry => Assert.Equal(HistoryStatus.Succeeded, entry.Status));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[null]")]
    public void CorruptHistoryIsPreservedInsteadOfBeingOverwritten(string corrupt)
    {
        File.WriteAllText(HistoryPath, corrupt);
        Assert.Throws<InvalidDataException>(() => Store.Query());
        Assert.Throws<InvalidDataException>(() => Store.Clear());
        Assert.Throws<InvalidDataException>(() => Store.Begin(HistoryAction.Download, NewRepo()));
        Assert.Equal(corrupt, File.ReadAllText(HistoryPath));
    }

    [Fact]
    public void FiltersAndPagingKeepFullResultCounts()
    {
        using (HistoryScope first = Store.Begin(HistoryAction.Download, NewRepo())) first.Complete(HistoryOutcome.Downloaded);
        using (HistoryScope second = Store.Begin(HistoryAction.Install, NewRepo())) second.Finish(HistoryStatus.Failed, HistoryOutcome.InstallFailed);
        using (HistoryScope third = Store.Begin(HistoryAction.Uninstall, NewRepo("other/repo"))) third.Complete(HistoryOutcome.Uninstalled);
        HistoryPage filtered = Store.Query(new(Repository: "OWNER/", Status: HistoryStatus.Failed,
            From: DateTimeOffset.UtcNow.AddDays(-1), To: DateTimeOffset.UtcNow.AddDays(1)));
        Assert.Equal(HistoryAction.Install, Assert.Single(filtered.Entries).Action);
        Assert.Empty(Store.Query(new(From: DateTimeOffset.UtcNow.AddDays(1))).Entries);
        Assert.Equal(3, Store.Query(new(Offset: 1, Limit: 1)).Total);
        Assert.Single(Store.Query(new(Offset: 1, Limit: 1)).Entries);
        Assert.Throws<ArgumentException>(() => Store.Query(new(Limit: 0)));
        Assert.Throws<ArgumentException>(() => Store.Query(new(From: DateTimeOffset.UtcNow, To: DateTimeOffset.UtcNow.AddDays(-1))));
    }

    [Fact]
    public void SkippedEntriesAndInactiveBatchesAreHiddenBeforePagingUnlessRequested()
    {
        using (HistoryScope success = Store.Begin(HistoryAction.Install, NewRepo())) success.Complete(HistoryOutcome.Installed);
        using (HistoryScope skipped = Store.Begin(HistoryAction.Update, NewRepo())) skipped.Finish(HistoryStatus.Skipped, HistoryOutcome.AlreadyCurrent);
        using (HistoryScope batch = Store.Begin(HistoryAction.Update, isBatch: true))
            batch.Finish(HistoryStatus.Succeeded, HistoryOutcome.BatchCompleted, skipped: 1);
        HistoryPage page = Store.Query(new(Limit: 1));
        Assert.Equal(1, page.Total);
        Assert.Equal(HistoryAction.Install, Assert.Single(page.Entries).Action);
        Assert.Empty(Store.Query(new(Offset: 1)).Entries);
        Assert.Equal(3, new HistoryStore(HistoryPath).Query(new(IncludeSkipped: true)).Total);
        Assert.Equal(HistoryStatus.Skipped, Assert.Single(Store.Query(new(Status: HistoryStatus.Skipped)).Entries).Status);
        Assert.DoesNotContain("skipped", new HistoryEntry { IsBatch = true, Outcome = HistoryOutcome.BatchCompleted, SkippedCount = 1 }.Summary);

        using StringWriter output = new();
        using StringWriter error = new();
        Assert.Equal(0, ArgHistory.Execute(["history", "--json"], output, error));
        using (JsonDocument json = JsonDocument.Parse(output.ToString()))
            Assert.Equal(1, json.RootElement.GetProperty("total").GetInt32());
        output.GetStringBuilder().Clear();
        Assert.Equal(0, ArgHistory.Execute(["history", "--include-skipped", "--json"], output, error));
        using (JsonDocument json = JsonDocument.Parse(output.ToString()))
            Assert.Equal(3, json.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task DownloadSuccessDoesNotBecomeInstallationSuccess()
    {
        Repo repo = NewRepo();
        repo.SaveFileAnyway = true;
        HistoryStatus status = await Run(repo, install: _ => Task.FromResult(false));
        Assert.Equal(HistoryStatus.Failed, status);
        Assert.Equal("v1", repo.CurrentInstallTag);
        Assert.Equal("v2", repo.LastDownloadTag);
        Assert.False(repo.IsUpToDate);
        HistoryEntry[] entries = Store.Query().Entries.ToArray();
        Assert.Contains(entries, entry => entry.Action == HistoryAction.Download && entry.Status == HistoryStatus.Succeeded);
        Assert.Contains(entries, entry => entry.Action == HistoryAction.Install && entry.Status == HistoryStatus.Failed);
        Assert.Contains(entries, entry => entry.ParentId is null && entry.Status == HistoryStatus.Failed);
    }

    [Fact]
    public async Task DownloadOnlySavesTheFileAndNeverRunsAnInstaller()
    {
        Repo repo = NewRepo();
        bool called = false;
        HistoryStatus status = await UpdateManager.RunRepositoryAsync(repo, _ => { }, _ => { }, downloadOnly: true,
            downloadFile: WriteDownload, installAsset: _ => { called = true; return Task.FromResult(true); }, cachePath: _root);
        Assert.Equal(HistoryStatus.Succeeded, status);
        Assert.False(called);
        Assert.Equal("v1", repo.CurrentInstallTag);
        Assert.Equal("v2", repo.LastDownloadTag);
        Assert.True(File.Exists(Path.Combine(repo.DownloadPath, "release.msi")));
        Assert.DoesNotContain(Store.Query().Entries, entry => entry.Action == HistoryAction.Install);
    }

    [Fact]
    public async Task PlainDownloadsHaveTheirOwnVersionAndRemainCurrentAfterRestart()
    {
        Repo repo = NewRepo();
        repo.ReplaceAssets(["release.zip"], ["private-url"]);
        repo.DownloadAssetIndex = 0;
        Assert.Equal(HistoryStatus.Succeeded, await Run(repo));
        Assert.Equal("v1", repo.CurrentInstallTag);
        Assert.Equal("v2", repo.LastDownloadTag);
        Repo restored = JsonSerializer.Deserialize<Repo>(JsonSerializer.Serialize(repo))!;
        Assert.True(restored.IsUpToDate);
        Assert.Equal("v2", restored.CurrentVersion);
        restored.AutoExtract = true;
        Assert.False(restored.IsUpToDate);
    }

    [Fact]
    public async Task PartialBatchRetainsIndividualResultsAndContinuesAfterInstallerFailure()
    {
        Repo failed = NewRepo("owner/failed");
        Repo succeeded = NewRepo("owner/succeeded");
        Repo excluded = NewRepo("owner/excluded");
        excluded.ExcludedFromDownloadAll = true;
        Repo current = NewRepo("owner/current");
        current.CurrentInstallTag = "v2";
        bool result = await UpdateManager.RunBatchAsync([failed, succeeded, excluded, current], _ => { }, _ => { },
            false, false, WriteDownload, asset => Task.FromResult(asset.Repo != failed), _root);
        Assert.False(result);
        Assert.Equal("v1", failed.CurrentInstallTag);
        Assert.Equal("v2", succeeded.CurrentInstallTag);
        HistoryEntry batch = Assert.Single(Store.Query().Entries, entry => entry.IsBatch);
        Assert.Equal(HistoryStatus.PartialFailure, batch.Status);
        Assert.Equal(1, batch.SucceededCount);
        Assert.Equal(1, batch.FailedCount);
        Assert.Equal(2, batch.SkippedCount);
        HistoryPage visible = new HistoryStore(HistoryPath).Query(new(BatchId: batch.Id, IncludeSteps: false, IncludeSkipped: true));
        Assert.Equal(5, visible.Total);
        Assert.Contains(visible.Entries, entry => entry.RepositoryName == "owner/failed" && entry.Status == HistoryStatus.Failed);
        Assert.Contains(visible.Entries, entry => entry.RepositoryName == "owner/succeeded" && entry.Status == HistoryStatus.Succeeded);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReinstallRecordsTheActualInstallerOutcomeEvenAtTheSameVersion(bool success)
    {
        Repo repo = NewRepo();
        repo.CurrentInstallTag = "v2";
        HistoryStatus status = await UpdateManager.RunRepositoryAsync(repo, _ => { }, _ => { }, force: true,
            downloadFile: WriteDownload, installAsset: _ => Task.FromResult(success), cachePath: _root);
        Assert.Equal(success ? HistoryStatus.Succeeded : HistoryStatus.Failed, status);
        HistoryEntry operation = Assert.Single(Store.Query().Entries, entry => entry.ParentId is null);
        Assert.Equal(HistoryAction.Reinstall, operation.Action);
        Assert.Equal("v2", operation.FromVersion);
        Assert.Equal("v2", operation.ToVersion);
    }

    [Fact]
    public async Task FailedRedownloadReportsFailureDespiteAPreviouslyDownloadedVersion()
    {
        Repo repo = NewRepo();
        repo.LastDownloadTag = "v2";
        Dictionary<Repo, HistoryStatus> outcomes = [];
        bool success = await UpdateManager.RunBatchAsync([repo], _ => { }, _ => { }, true, true,
            (_, _, _) => throw new IOException("private transport failure"), cachePath: _root,
            completed: (item, status) => outcomes[item] = status);
        Assert.False(success);
        Assert.Equal("v2", repo.LastDownloadTag);
        Assert.Equal(HistoryStatus.Failed, outcomes[repo]);
        Assert.Contains(Store.Query().Entries, entry => entry.Action == HistoryAction.Download
            && entry.Outcome == HistoryOutcome.DownloadFailed);
    }

    [Fact]
    public async Task ExplicitReinstallCanIncludeRepositoriesExcludedFromAutomaticBatches()
    {
        Repo repo = NewRepo();
        repo.ExcludedFromDownloadAll = true;
        bool installed = false;
        bool success = await UpdateManager.RunBatchAsync([repo], _ => { }, _ => { }, true, false,
            WriteDownload, _ => { installed = true; return Task.FromResult(true); }, _root, respectExclusions: false);
        Assert.True(success);
        Assert.True(installed);
        Assert.Equal("v2", repo.CurrentInstallTag);
    }

    [Fact]
    public async Task HistoryNeverCopiesPrivateUrlsPasswordsOrInstallerExceptionText()
    {
        const string secret = "private-secret-value-928371";
        Repo repo = NewRepo();
        repo.Url = "https://private.example/repo?token=" + secret;
        repo.DownloadUrls = [repo.Url];
        UpdateManager.RootPassword = secret;
        try
        {
            await Run(repo, install: _ => throw new IOException("Installer output: " + repo.Url + " password=" + secret));
            string json = File.ReadAllText(HistoryPath);
            Assert.DoesNotContain(secret, json);
            Assert.DoesNotContain("https://", json);
            Assert.DoesNotContain("Installer output", json);
            Assert.Contains(Store.Query().Entries, entry => entry.Outcome == HistoryOutcome.InstallFailed);
        }
        finally { UpdateManager.RootPassword = null; }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(126)]
    public async Task UninstallHistoryMatchesRemovalResultAndKeepsOldVersionOnFailure(int exitCode)
    {
        if (!OperatingSystem.IsLinux()) return;
        Repo repo = NewRepo();
        repo.InstalledDebianPackage = "actual-package";
        InstallationManager manager = new(Path.Combine(_root, "app-images"), Path.Combine(_root, "applications"),
            (_, _) => Task.FromResult(new InstallationManager.CommandResult(exitCode, "private installer output")));
        bool success = await manager.UninstallAsync(repo, _ => { }, _ => { });
        Assert.Equal(exitCode == 0, success);
        HistoryEntry entry = Assert.Single(Store.Query().Entries);
        Assert.Equal(HistoryAction.Uninstall, entry.Action);
        Assert.Equal(exitCode == 0 ? HistoryStatus.Succeeded : HistoryStatus.Failed, entry.Status);
        Assert.Equal(exitCode == 0 ? "" : "v1", repo.CurrentInstallTag);
        Assert.DoesNotContain("private installer output", File.ReadAllText(HistoryPath));
    }

    [Fact]
    public void CliJsonIsStructuredAndClearRequiresExplicitConfirmation()
    {
        using (HistoryScope operation = Store.Begin(HistoryAction.Install, NewRepo())) operation.Complete(HistoryOutcome.Installed);
        using StringWriter output = new();
        using StringWriter error = new();
        Assert.Equal(0, ArgHistory.Execute(["history", "--json", "--status", "Succeeded"], output, error));
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        Assert.Equal(1, json.RootElement.GetProperty("total").GetInt32());
        Assert.Empty(error.ToString());
        Assert.Equal(2, ArgHistory.Execute(["history", "clear"], output, error));
        Assert.Equal(1, Store.Query().Total);
        Assert.Equal(0, ArgHistory.Execute(["history", "clear", "--yes"], output, error));
        Assert.Equal(0, Store.Query().Total);
    }

    [Fact]
    public async Task CliHistoryDoesNotLoadRepositoriesInitializeTokensOrClearDownloadCache()
    {
        if (!OperatingSystem.IsLinux()) return;
        string data = Path.Combine(_root, "data");
        string config = Path.Combine(_root, "config");
        string cache = Path.Combine(_root, "cache");
        string download = Path.Combine(cache, "github-downloader", "in-progress");
        Directory.CreateDirectory(Path.GetDirectoryName(download)!);
        File.WriteAllText(download, "keep");
        HistoryStore childStore = new(Path.Combine(data, "github-downloader", "history.json"));
        using (HistoryScope operation = childStore.Begin(HistoryAction.Install, NewRepo())) operation.Complete(HistoryOutcome.Installed);
        ProcessStartInfo start = new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(Github_Downloader_cli.Program).Assembly.Location);
        start.ArgumentList.Add("history");
        start.ArgumentList.Add("--json");
        start.Environment["XDG_DATA_HOME"] = data;
        start.Environment["XDG_CONFIG_HOME"] = config;
        start.Environment["XDG_CACHE_HOME"] = cache;
        using Process process = new() { StartInfo = start };
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(await error);
        using JsonDocument json = JsonDocument.Parse(await output);
        Assert.Equal(1, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("keep", File.ReadAllText(download));
        Assert.False(Directory.Exists(Path.Combine(data, "github-downloader", "logs")));
        Assert.False(Directory.Exists(config));
    }

    private Task<HistoryStatus> Run(Repo repo, Func<UpdateManager.Asset, Task<bool>>? install = null) =>
        UpdateManager.RunRepositoryAsync(repo, _ => { }, _ => { }, downloadFile: WriteDownload,
            installAsset: install ?? (_ => Task.FromResult(true)), cachePath: _root);

    private static Task WriteDownload(string url, string path, IProgress<double> progress)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return File.WriteAllTextAsync(path, "downloaded installer");
    }

    private Repo NewRepo(string name = "owner/repo")
    {
        Repo repo = new()
        {
            Name = name, Url = "https://example.invalid/" + name, Tag = "v2", CurrentInstallTag = "v1",
            DownloadPath = Path.Combine(_root, name.Replace('/', '-'))
        };
        Directory.CreateDirectory(repo.DownloadPath);
        repo.ReplaceAssets(["release.msi"], ["https://private.invalid/download?token=private"]);
        repo.DownloadAssetIndex = 0;
        return repo;
    }

    public void Dispose()
    {
        OperationHistory.Store = _previous;
        Directory.Delete(_root, recursive: true);
    }
}
