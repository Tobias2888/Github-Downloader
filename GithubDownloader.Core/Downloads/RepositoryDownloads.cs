using GithubDownloader.Core.Operations;
using GithubDownloader.Core.Installation.Archives;
using GithubDownloader.Core.History;
using GithubDownloader.Core.GitHub;
using GithubDownloader.Core.Configuration;
using GithubDownloader.Core.Models;
using LoggerLib;

namespace GithubDownloader.Core.Downloads;

using Asset = GithubDownloader.Core.Operations.RepositoryAsset;

internal sealed class RepositoryDownloads(HistoryStore historyStore, Func<string> token)
{
    internal async Task<Asset?> DownloadAsset(Repo repo, Action<string> statusText, Action<string> progressText,
        Guid? parentId = null, Guid? batchId = null,
        Func<string, string, IProgress<double>, Task>? downloadFile = null, string? cachePath = null,
        Action<DownloadProgress>? transferProgress = null, CancellationToken cancellationToken = default, Action<OperationProgress>? operationProgress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Logger.LogI("Preparing asset download");

        using HistoryScope history = historyStore.Begin(HistoryAction.Download, repo, parentId, batchId: batchId);
        operationProgress?.Invoke(new OperationProgress(OperationPhase.Download, null, StartsItem: true));
        statusText.Invoke($"Downloading {repo.Name}");

        InlineProgress progress = new(percent =>
        {
            if (operationProgress is not null) operationProgress(new OperationProgress(OperationPhase.Download, percent));
            else progressText($"Downloaded: {percent:0.00}%");
        });

        string downloadAssetName = repo.SelectedAssetName;
        string downloadAssetUrl = repo.SelectedAssetUrl;
        if (downloadAssetName == "" || downloadAssetUrl == "")
        {
            _ = Logger.LogW("No release asset selected for download");
            string message = $"Skipped {repo.Name}: {repo.AssetSelectionStatus}";
            statusText.Invoke(message);
            progressText.Invoke(message);
            history.Finish(HistoryStatus.Failed, HistoryOutcome.SelectionUnresolved);
            return null;
        }

        string downloadTag = repo.Tag;
        cachePath ??= FileManager.CachePath;
        string? temporaryDirectory = repo.AutoExtract && ArchiveExtractor.IsSupportedArchive(downloadAssetName)
            || downloadAssetName.EndsWith(".deb", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(cachePath, $"archive-{Guid.NewGuid():N}")
            : null;
        string temporaryPath = Path.Combine(temporaryDirectory ?? cachePath, downloadAssetName);
        if (temporaryDirectory != null) Directory.CreateDirectory(temporaryDirectory);
        try
        {
            if (downloadFile is null)
                await Api.DownloadFileAsync(downloadAssetUrl, temporaryPath, token(), transferProgress: snapshot =>
                {
                    if (operationProgress is not null) operationProgress(OperationProgress.Download(snapshot));
                    else if (transferProgress is null) progressText(snapshot.DisplayText);
                    transferProgress?.Invoke(snapshot);
                }, cancellationToken: cancellationToken);
            else await downloadFile(downloadAssetUrl, temporaryPath, progress);
            cancellationToken.ThrowIfCancellationRequested();
            history.Complete(HistoryOutcome.Downloaded);
        }
        catch
        {
            history.Finish(cancellationToken.IsCancellationRequested ? HistoryStatus.Interrupted : HistoryStatus.Failed,
                cancellationToken.IsCancellationRequested ? HistoryOutcome.Interrupted : HistoryOutcome.DownloadFailed);
            if (temporaryDirectory != null) Directory.Delete(temporaryDirectory, recursive: true);
            else if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }

        Asset asset = new()
        {
            Repo = repo,
            TempAssetPath = temporaryPath,
            Tag = downloadTag,
            TemporaryDirectory = temporaryDirectory,
            ParentHistoryId = parentId,
            BatchId = batchId
        };

        return asset;
    }

    internal static void CopyFile(Asset asset)
    {
        string destName = asset.Repo.NewFileName == "" ? asset.Repo.SelectedAssetName : asset.Repo.NewFileName;
        string destPath = Path.Join(asset.Repo.DownloadPath, destName);
        if (File.Exists(destPath))
        {
            File.Delete(destPath);
        }

        _ = Logger.LogI("Copying downloaded asset");
        File.Copy(Path.Join(asset.TempAssetPath), destPath);
        _ = Logger.LogI("Downloaded asset copied");
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
