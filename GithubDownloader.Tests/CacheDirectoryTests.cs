using GithubDownloader.Core;
using GithubDownloader.Core.History;
using GithubDownloader.Core.Models;
using Xunit;

namespace GithubDownloader.Tests;

public sealed class CacheDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ghd-cache-test-" + Guid.NewGuid().ToString("N"));
    private readonly HistoryStore _previousHistory = OperationHistory.Store;
    private string CachePath => Path.Combine(_root, "cache");

    public CacheDirectoryTests()
    {
        Directory.CreateDirectory(_root);
        OperationHistory.Store = new HistoryStore(Path.Combine(_root, "history.json"));
    }

    [Fact]
    public async Task PlainAssetDownloadRecreatesCacheDeletedWhileRunning()
    {
        Directory.CreateDirectory(CachePath);
        Directory.Delete(CachePath, recursive: true);

        Repo repo = new()
        {
            Name = "owner/repo",
            Url = "https://example.invalid/owner/repo",
            Tag = "v2",
            CurrentInstallTag = "v1",
            DownloadPath = Path.Combine(_root, "downloads")
        };
        Directory.CreateDirectory(repo.DownloadPath);
        repo.ReplaceAssets(["release.bin"], ["https://example.invalid/release.bin"]);
        repo.DownloadAssetIndex = 0;

        string? downloadPath = null;
        HistoryStatus status = await UpdateManager.RunRepositoryAsync(repo, _ => { }, _ => { },
            downloadFile: (_, path, _) =>
            {
                downloadPath = path;
                return File.WriteAllTextAsync(path, "downloaded asset");
            }, cachePath: CachePath);

        Assert.Equal(HistoryStatus.Succeeded, status);
        Assert.Equal(Path.Combine(CachePath, "release.bin"), downloadPath);
        Assert.Equal("downloaded asset", await File.ReadAllTextAsync(Path.Combine(CachePath, "release.bin")));
        Assert.Equal("downloaded asset", await File.ReadAllTextAsync(Path.Combine(repo.DownloadPath, "release.bin")));
    }

    public void Dispose()
    {
        OperationHistory.Store = _previousHistory;
        Directory.Delete(_root, recursive: true);
    }
}
