using GithubDownloader.Core.Repositories;
using GithubDownloader.Core.Operations;
using GithubDownloader.Core.Installation.Launchers;
using GithubDownloader.Core.Installation;
using GithubDownloader.Core.History;
using GithubDownloader.Core.GitHub;
using GithubDownloader.Core.Downloads;
using GithubDownloader.Core.Configuration;
using System.Collections.ObjectModel;
using System.Diagnostics;
using GithubDownloader.Core.Models;
using GithubDownloader.Core.Enums;
using SecretsLib;

namespace GithubDownloader.Core;

using Asset = GithubDownloader.Core.Operations.RepositoryAsset;

/// <summary>Application entry points; services own release lookup, operations, and repository subscriptions.</summary>
public static class UpdateManager
{
    private static readonly RepositoryCatalog Catalog = new(FileManager.SaveRepos);
    public static ObservableCollection<Repo> Repos { get => Catalog.Repos; set => Catalog.Repos = value; }
    public static Platform CurPlatform { get; set; }
    public static string? RootPassword { get; set; }
    public static WindowsSelfUpdate SelfUpdate { get; } = new();
    public static void WatchRepos() => Catalog.Watch();

    private static ReleaseService Releases => new(url => Api.GetRequest(url, SecretsManager.LookupSecret("pat") ?? ""));
    public static Task<Repo?> AddRepo(string repoUrl) => Releases.AddRepo(repoUrl);
    public static Task<Repo?> AddRepo(string publisherName, string repoName) => Releases.AddRepo(publisherName, repoName);
    public static Task UpdateRepoDetails(IEnumerable<Repo> repos) => Releases.UpdateRepoDetails(repos);
    public static Task<bool> SearchForUpdates(IEnumerable<Repo> repos, Action<string> statusText) => Releases.SearchForUpdates(repos, statusText);
    public static Task<bool> SearchForUpdates(Repo repo, Action<string> statusText, bool multiDownload = false) => Releases.SearchForUpdates(repo, statusText, multiDownload);
    internal static Task<bool> SearchForUpdates(IEnumerable<Repo> repos, Action<string> statusText, Func<string, Task<HttpResponseMessage?>> request) =>
        new ReleaseService(request).SearchForUpdates(repos, statusText);
    internal static Task<bool> SearchForUpdates(Repo repo, Action<string> statusText, Func<string, Task<HttpResponseMessage?>> request, bool multiDownload = false) =>
        new ReleaseService(request).SearchForUpdates(repo, statusText, multiDownload);

    public static bool IsInstallerAsset(string name) => PackageInstaller.IsInstallerAsset(name);
    internal static bool IsInstallerAsset(string name, bool linux, bool debian, bool arch = false) =>
        PackageInstaller.IsInstallerAsset(name, linux, debian, arch);

    public static async Task<bool> UpdateRepo(Repo repo, Action<string> statusText,
        Action<string> progressText, bool downloadAnyways = false, Action<DownloadProgress>? transferProgress = null, CancellationToken cancellationToken = default,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectArchiveExecutable = null, Action<OperationProgress>? operationProgress = null)
        => await RunRepositoryAsync(repo, statusText, progressText, downloadAnyways, transferProgress: transferProgress, cancellationToken: cancellationToken, selectArchiveExecutable: selectArchiveExecutable, operationProgress: operationProgress) is not HistoryStatus.Failed;

    public static Task<bool> UpdateReposAsync(IEnumerable<Repo> repos, Action<string> statusText,
        Action<string> progressText, bool downloadAnyways = false,
        Action<Repo, HistoryStatus>? completed = null, bool respectExclusions = true,
        Action<DownloadProgress>? transferProgress = null, CancellationToken cancellationToken = default,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectArchiveExecutable = null, Action<OperationProgress>? operationProgress = null, InstallationContext? installation = null) =>
        RunBatchAsync(repos, statusText, progressText, downloadAnyways, downloadOnly: false,
            completed: completed, respectExclusions: respectExclusions, transferProgress: transferProgress, cancellationToken: cancellationToken, selectArchiveExecutable: selectArchiveExecutable, operationProgress: operationProgress, installation: installation);

    public static Task<bool> DownloadReposAsync(IEnumerable<Repo> repos, Action<string> statusText,
        Action<string> progressText, bool downloadAnyways = false, Action<Repo, HistoryStatus>? completed = null,
        Action<DownloadProgress>? transferProgress = null, CancellationToken cancellationToken = default, Action<OperationProgress>? operationProgress = null) =>
        RunBatchAsync(repos, statusText, progressText, downloadAnyways, downloadOnly: true, completed: completed, transferProgress: transferProgress, cancellationToken: cancellationToken, operationProgress: operationProgress);

    private static RepositoryOperations Operations => CreateOperations();
    private static RepositoryOperations CreateOperations(InstallationContext? installation = null) => new(OperationHistory.Store, SelfUpdate,
        installation is null ? Packages : new PackageInstaller(installation.Platform, installation.RootPassword),
        new RepositoryDownloads(OperationHistory.Store, () => SecretsManager.LookupSecret("pat") ?? ""));
    internal static Task<bool> RunBatchAsync(IEnumerable<Repo> repos, Action<string> statusText,
        Action<string> progressText, bool force, bool downloadOnly,
        Func<string, string, IProgress<double>, Task>? downloadFile = null,
        Func<Asset, Task<bool>>? installAsset = null, string? cachePath = null,
        Action<Repo, HistoryStatus>? completed = null, bool respectExclusions = true,
        Action<DownloadProgress>? transferProgress = null,
        Func<IReadOnlyList<Asset>, Task<bool>>? installDebianAssets = null, CancellationToken cancellationToken = default,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectArchiveExecutable = null, Action<OperationProgress>? operationProgress = null, InstallationContext? installation = null) =>
        CreateOperations(installation).RunBatchAsync(repos, statusText, progressText, force, downloadOnly, downloadFile, installAsset,
            cachePath, completed, respectExclusions, transferProgress, installDebianAssets, cancellationToken, selectArchiveExecutable, operationProgress);

    internal static Task<HistoryStatus> RunRepositoryAsync(Repo repo, Action<string> statusText,
        Action<string> progressText, bool force = false, bool downloadOnly = false,
        Guid? batchId = null, bool respectExclusion = false,
        Func<string, string, IProgress<double>, Task>? downloadFile = null,
        Func<Asset, Task<bool>>? installAsset = null, string? cachePath = null,
        Action<DownloadProgress>? transferProgress = null, CancellationToken cancellationToken = default,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectArchiveExecutable = null, Action<OperationProgress>? operationProgress = null) =>
        Operations.RunRepositoryAsync(repo, statusText, progressText, force, downloadOnly, batchId, respectExclusion,
            downloadFile, installAsset, cachePath, transferProgress, cancellationToken, selectArchiveExecutable, operationProgress);

    internal static Task<bool> UpdateReposAsync(List<Asset?> assets, Action<string> statusText,
        Action<string> progressText, Func<Asset, Task<bool>>? installAsset = null) =>
        Operations.UpdateReposAsync(assets, statusText, progressText, installAsset);

    internal static Task<bool> ApplyAssetAsync(Asset asset, Action<string> statusText,
        Action<string> progressText, bool downloadOnly, Func<Asset, Task<bool>>? installAsset,
        Func<string, IReadOnlyList<string>, Task<string?>>? selectArchiveExecutable = null,
        ArchiveAppLauncher? archiveLauncher = null, Action<OperationProgress>? operationProgress = null) =>
        Operations.ApplyAssetAsync(asset, statusText, progressText, downloadOnly, installAsset, selectArchiveExecutable, archiveLauncher, operationProgress);

    private static PackageInstaller Packages => new(CurPlatform, RootPassword);
    internal static ProcessStartInfo CreatePackageInstallStartInfo(string manager) => Packages.CreatePackageInstallStartInfo(manager);
    internal static Task<bool> InstallArchAssetAsync(Asset asset, Action<string> progress, InstallationManager? installations = null,
        Func<ProcessStartInfo, Action<string>, Task<int>>? run = null) => Packages.InstallArchAssetAsync(asset, progress, installations, run);
    internal static Task<bool> InstallArchPackageAsync(string path, Action<string> progress,
        Func<ProcessStartInfo, Action<string>, Task<int>>? run = null) => Packages.InstallArchPackageAsync(path, progress, run);
    internal static Task<int> RunPackageInstallCommandAsync(ProcessStartInfo start, Action<string> progress) => Packages.RunPackageInstallCommandAsync(start, progress);
}
