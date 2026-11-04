using GithubDownloader.Core.Installation.Launchers;
using GithubDownloader.Core.Installation.Archives;
using GithubDownloader.Core.Installation;
using GithubDownloader.Core.History;
using GithubDownloader.Core.Downloads;
using GithubDownloader.Core.Models;
using GithubDownloader.Core.Enums;
using LoggerLib;

namespace GithubDownloader.Core.Operations;

using Asset = GithubDownloader.Core.Operations.RepositoryAsset;

/// <summary>Coordinates downloads, batch installation and history using per-operation dependencies.</summary>
internal sealed class RepositoryOperations(HistoryStore history, WindowsSelfUpdate selfUpdate, PackageInstaller packages, RepositoryDownloads downloads)
{
    private readonly RepositoryDownloads _downloads = downloads;
    private readonly HistoryStore _history = history;
    private readonly WindowsSelfUpdate _selfUpdate = selfUpdate;
    private readonly PackageInstaller _packages = packages;
    internal async Task<bool> RunBatchAsync(IEnumerable<Repo> repos, Action<string> statusText,
        Action<string> progressText, bool force, bool downloadOnly,
        Func<string, string, IProgress<double>, Task>? downloadFile = null,
        Func<Asset, Task<bool>>? installAsset = null, string? cachePath = null,
        Action<Repo, HistoryStatus>? completed = null, bool respectExclusions = true,
        Action<DownloadProgress>? transferProgress = null,
        Func<IReadOnlyList<Asset>, Task<bool>>? installDebianAssets = null, CancellationToken cancellationToken = default,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectArchiveExecutable = null, Action<OperationProgress>? operationProgress = null)
    {
        Repo[] snapshot = WindowsSelfUpdate.OrderRepositories(repos, downloadOnly);
        using HistoryScope batch = _history.Begin(downloadOnly ? HistoryAction.Download
            : force ? HistoryAction.Reinstall : HistoryAction.Update, isBatch: true);
        int succeeded = 0;
        int failed = 0;
        int skipped = 0;
        statusText("Downloading updates...");
        Repo[] debianRepos = !downloadOnly && InstallationManager.CanInstallDebianPackages && installAsset is null
            ? snapshot.Where(repo => (!respectExclusions || !repo.ExcludedFromDownloadAll)
                && (force || !repo.IsUpToDate)
                && repo.SelectedAssetName.EndsWith(".deb", StringComparison.OrdinalIgnoreCase)).ToArray()
            : [];
        try
        {
            Dictionary<Repo, HistoryStatus> debianResults = await RunDebianBatchAsync(debianRepos,
                statusText, progressText, force, batch.Id, downloadFile, cachePath, transferProgress,
                installDebianAssets, cancellationToken, operationProgress);
            foreach (Repo repo in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HistoryStatus result = debianResults.TryGetValue(repo, out HistoryStatus debianResult)
                    ? debianResult : await RunRepositoryAsync(repo, statusText, progressText, force,
                    downloadOnly, batch.Id, respectExclusion: !downloadOnly && respectExclusions,
                    downloadFile: downloadFile, installAsset: installAsset, cachePath: cachePath, transferProgress: transferProgress, cancellationToken: cancellationToken,
                    selectArchiveExecutable: selectArchiveExecutable, operationProgress: operationProgress);
                completed?.Invoke(repo, result);
                if (result == HistoryStatus.Skipped) skipped++;
                else if (result == HistoryStatus.Succeeded) succeeded++;
                else failed++;
            }
            batch.Finish(failed == 0 ? HistoryStatus.Succeeded : succeeded > 0 ? HistoryStatus.PartialFailure
                : HistoryStatus.Failed, HistoryOutcome.BatchCompleted, succeeded, failed, skipped);
            return failed == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            batch.Finish(HistoryStatus.Interrupted, HistoryOutcome.Interrupted, succeeded, failed, skipped);
            throw;
        }
    }

    private async Task<Dictionary<Repo, HistoryStatus>> RunDebianBatchAsync(Repo[] repositories,
        Action<string> statusText, Action<string> progressText, bool force, Guid batchId,
        Func<string, string, IProgress<double>, Task>? downloadFile, string? cachePath,
        Action<DownloadProgress>? transferProgress, Func<IReadOnlyList<Asset>, Task<bool>>? installDebianAssets,
        CancellationToken cancellationToken, Action<OperationProgress>? operationProgress)
    {
        Dictionary<Repo, HistoryStatus> results = [];
        List<(Asset Asset, HistoryScope Operation)> prepared = [];
        try
        {
            foreach (Repo repo in repositories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HistoryScope operation = _history.Begin(force ? HistoryAction.Reinstall
                    : HistoryAction.Update, repo, batchId, batchId: batchId);
                try
                {
                    Asset? asset = await _downloads.DownloadAsset(repo, statusText, progressText, operation.Id,
                        batchId, downloadFile, cachePath, transferProgress, cancellationToken, operationProgress);
                    if (asset.HasValue)
                    {
                        prepared.Add((asset.Value, operation));
                        continue;
                    }
                    operation.Finish(HistoryStatus.Failed, HistoryOutcome.SelectionUnresolved);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    operation.Finish(HistoryStatus.Interrupted, HistoryOutcome.Interrupted);
                    operation.Dispose();
                    throw;
                }
                catch (Exception exception)
                {
                    _ = Logger.LogE($"Repository download failed ({exception.GetType().Name})");
                    progressText("Repository download failed. Check connectivity and permissions.");
                    operation.Finish(HistoryStatus.Failed, HistoryOutcome.DownloadFailed);
                }
                operation.Dispose();
                results[repo] = HistoryStatus.Failed;
            }

            cancellationToken.ThrowIfCancellationRequested();
            Asset[] assets = prepared.Select(item => item.Asset).ToArray();
            bool installed = false;
            if (assets.Length > 0)
            {
                try
                {
                    // All packages share one apt-get process and one authorization request.
                    installed = await (installDebianAssets is null
                        ? InstallDebianAssetsAsync(assets, progressText) : installDebianAssets(assets));
                }
                catch (Exception exception)
                {
                    _ = Logger.LogE($"Debian batch installation failed ({exception.GetType().Name})");
                    progressText("Debian package installation failed. Check permissions and installation requirements.");
                }
            }
            foreach ((Asset asset, HistoryScope operation) in prepared)
            {
                bool success = await ApplyAssetAsync(asset, statusText, progressText, false,
                    _ => Task.FromResult(installed));
                HistoryStatus result = success ? HistoryStatus.Succeeded : HistoryStatus.Failed;
                operation.Finish(result, success ? HistoryOutcome.Installed : HistoryOutcome.InstallFailed);
                results[asset.Repo] = result;
            }
            return results;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            foreach ((Asset asset, HistoryScope operation) in prepared)
                operation.Finish(HistoryStatus.Interrupted, HistoryOutcome.Interrupted);
            throw;
        }
        finally
        {
            foreach ((Asset asset, HistoryScope operation) in prepared)
            {
                operation.Dispose();
                if (asset.TemporaryDirectory is string directory && Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }
    }

    private async Task<bool> InstallDebianAssetsAsync(IReadOnlyList<Asset> assets, Action<string> progressText)
    {
        InstallationManager installations = new();
        Dictionary<Repo, string> packages = [];
        foreach (Asset asset in assets)
            packages[asset.Repo] = await installations.ReadDebianPackageNameAsync(asset.TempAssetPath);
        if (!await _packages.InstallDebsAsync(assets.Select(asset => asset.TempAssetPath).ToList(), progressText)) return false;
        foreach (Asset asset in assets) asset.Repo.InstalledDebianPackage = packages[asset.Repo];
        return true;
    }

    internal async Task<HistoryStatus> RunRepositoryAsync(Repo repo, Action<string> statusText,
        Action<string> progressText, bool force = false, bool downloadOnly = false,
        Guid? batchId = null, bool respectExclusion = false,
        Func<string, string, IProgress<double>, Task>? downloadFile = null,
        Func<Asset, Task<bool>>? installAsset = null, string? cachePath = null,
        Action<DownloadProgress>? transferProgress = null, CancellationToken cancellationToken = default,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectArchiveExecutable = null, Action<OperationProgress>? operationProgress = null)
    {
        using HistoryScope operation = _history.Begin(downloadOnly ? HistoryAction.Download
            : force ? HistoryAction.Reinstall : HistoryAction.Update, repo, batchId, batchId: batchId);
        if (respectExclusion && repo.ExcludedFromDownloadAll || !force
            && (downloadOnly ? repo.LastDownloadTag == repo.Tag : repo.IsUpToDate))
        {
            operation.Finish(HistoryStatus.Skipped, respectExclusion && repo.ExcludedFromDownloadAll
                ? HistoryOutcome.Excluded : HistoryOutcome.AlreadyCurrent);
            return HistoryStatus.Skipped;
        }
        bool downloaded = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Asset? asset = await _downloads.DownloadAsset(repo, statusText, progressText,
                operation.Id, batchId, downloadFile, cachePath, transferProgress, cancellationToken, operationProgress);
            if (asset is null)
            {
                operation.Finish(HistoryStatus.Failed, HistoryOutcome.SelectionUnresolved);
                return HistoryStatus.Failed;
            }
            downloaded = true;
            bool success = await ApplyAssetAsync(asset.Value, statusText, progressText, downloadOnly, installAsset, selectArchiveExecutable, operationProgress: operationProgress);
            bool extract = !downloadOnly && repo.AutoExtract && ArchiveExtractor.IsSupportedArchive(asset.Value.TempAssetPath);
            bool install = !downloadOnly && PackageInstaller.IsInstallerAsset(asset.Value.TempAssetPath);
            bool handedOff = success && _selfUpdate.ShutdownRequested && WindowsSelfUpdate.IsSelfUpdate(repo);
            operation.Finish(success ? HistoryStatus.Succeeded : HistoryStatus.Failed, success
                ? handedOff ? HistoryOutcome.InstallerLaunched
                    : extract ? HistoryOutcome.Extracted : install ? HistoryOutcome.Installed : HistoryOutcome.Saved
                : extract ? HistoryOutcome.ExtractionFailed : install ? HistoryOutcome.InstallFailed : HistoryOutcome.SaveFailed);
            return success ? HistoryStatus.Succeeded : HistoryStatus.Failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operation.Finish(HistoryStatus.Interrupted, HistoryOutcome.Interrupted);
            throw;
        }
        catch (Exception exception)
        {
            _ = Logger.LogE($"Repository operation failed ({exception.GetType().Name})");
            progressText("Repository operation failed. Check connectivity, permissions, and installation requirements.");
            operation.Finish(HistoryStatus.Failed, downloaded ? HistoryOutcome.Failed : HistoryOutcome.DownloadFailed);
            return HistoryStatus.Failed;
        }
    }

    internal async Task<bool> UpdateReposAsync(List<Asset?> assets, Action<string> statusText,
        Action<string> progressText, Func<Asset, Task<bool>>? installAsset = null)
    {
        bool succeeded = true;
        foreach (Asset? asset in assets)
            if (asset.HasValue)
                succeeded &= await ApplyAssetAsync(asset.Value, statusText, progressText, false, installAsset);
        return succeeded;
    }

    internal async Task<bool> ApplyAssetAsync(Asset asset, Action<string> statusText,
        Action<string> progressText, bool downloadOnly, Func<Asset, Task<bool>>? installAsset,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectArchiveExecutable = null,
        ArchiveAppLauncher? archiveLauncher = null, Action<OperationProgress>? operationProgress = null)
    {
        bool extract = !downloadOnly && asset.Repo.AutoExtract && ArchiveExtractor.IsSupportedArchive(asset.TempAssetPath);
        bool install = !downloadOnly && PackageInstaller.IsInstallerAsset(asset.TempAssetPath);
        string displayName = asset.LocalSourcePath is null ? asset.Repo.Name : Path.GetFileName(asset.LocalSourcePath);
        using HistoryScope history = _history.Begin(extract ? HistoryAction.Extract
            : install ? HistoryAction.Install : HistoryAction.Save, asset.Repo, asset.ParentHistoryId,
            batchId: asset.BatchId, toVersion: asset.Tag, localFileName: asset.LocalSourcePath is null ? null : Path.GetFileName(asset.LocalSourcePath));
        try
        {
            if (asset.LocalSourcePath is string source)
            {
                statusText("Preparing local file...");
                Directory.CreateDirectory(asset.TemporaryDirectory!);
                await Task.Run(() => File.Copy(source, asset.TempAssetPath));
            }
            if (extract)
            {
                statusText($"Extracting {displayName}");
                if (asset.LocalSourcePath is null && OperatingSystem.IsLinux() && asset.Repo.CreateArchiveLauncher)
                    await RepositoryArchiveInstaller.InstallAsync(asset.Repo, asset.TempAssetPath, asset.Tag,
                        progressText, selectArchiveExecutable, archiveLauncher, operationProgress);
                else
                    await ArchiveExtractor.ExtractDownloadAsync(asset.Repo, asset.TempAssetPath, asset.Tag, progressText,
                        asset.ExtractionDirectory, asset.LocalSourcePath, asset.ExtractionFileWritten, operationProgress);
            }
            else
            {
                if (!install || asset.Repo.SaveFileAnyway)
                {
                    statusText($"Move file {displayName}");
                    RepositoryDownloads.CopyFile(asset);
                    asset.Repo.LastDownloadTag = asset.Tag;
                }
                if (install)
                {
                    statusText($"Installing {displayName}");
                    if (installAsset is null && asset.LocalSourcePath is null && WindowsSelfUpdate.IsSelfUpdate(asset.Repo))
                    {
                        _selfUpdate.Launch(asset.TempAssetPath);
                        progressText("Installer started. GitHub Downloader will exit to complete its update. Leave 'Open GitHub Downloader' checked on the finish page to reopen it.");
                        history.Complete(HistoryOutcome.InstallerLaunched);
                        return true;
                    }
                    if (!await (installAsset is null
                        ? _packages.InstallAssetAsync(asset, progressText) : installAsset(asset)))
                    {
                        history.Finish(HistoryStatus.Failed, HistoryOutcome.InstallFailed);
                        return false;
                    }
                    asset.Repo.CurrentInstallTag = asset.Tag;
                }
            }
            history.Complete(extract ? HistoryOutcome.Extracted : install ? HistoryOutcome.Installed : HistoryOutcome.Saved);
            return true;
        }
        catch (Exception exception)
        {
            string action = extract ? "Extraction" : install ? "Installation" : "Saving downloaded file";
            _ = Logger.LogE($"{action} failed ({exception.GetType().Name})");
            progressText($"{action} failed ({exception.GetType().Name})");
            history.Finish(HistoryStatus.Failed, extract ? HistoryOutcome.ExtractionFailed
                : install ? HistoryOutcome.InstallFailed : HistoryOutcome.SaveFailed);
            return false;
        }
        finally
        {
            if (asset.TemporaryDirectory is string directory && Directory.Exists(directory))
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (IOException) { _ = Logger.LogW("Temporary download cleanup failed"); }
                catch (UnauthorizedAccessException) { _ = Logger.LogW("Temporary download cleanup failed"); }
            }
        }
    }

}
