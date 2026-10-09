using System.ComponentModel.DataAnnotations;

namespace Github_Downloader_Web.DTOs;

public record AddRepoRequest(
    [Required] string RepoUrl
);

public record AddRepoByNameRequest(
    [Required] string PublisherName,
    [Required] string RepoName
);

public record RepoResponse(
    string Url,
    string Name,
    string Description,
    int DownloadAssetIndex,
    List<string> AssetNames,
    List<string> DownloadUrls,
    string Tag,
    string CurrentInstallTag,
    string TargetTag,
    List<string> Tags,
    string ReleaseDate,
    string GitHubLink,
    string LatestChangelog,
    bool IsUpToDate,
    string DownloadPath,
    bool SaveFileAnyway,
    string NewFileName,
    bool ExcludedFromDownloadAll,
    bool IsSelected
);

public record UpdateRepoRequest(
    string DownloadPath,
    bool? SaveFileAnyway,
    string? NewFileName,
    bool? ExcludedFromDownloadAll,
    string? TargetTag,
    int? DownloadAssetIndex
);

public record ReposListResponse(
    List<RepoResponse> Repos,
    int TotalCount
);

public record ExportConfigRequest(
    [Required] string DestinationPath
);

public record ImportConfigRequest(
    [Required] string SourcePath
);

public record ConfigOperationResponse(
    bool Success,
    string Message
);

public record SearchUpdatesRequest(
    List<string>? RepoUrls = null
);

public record SearchUpdatesResponse(
    List<RepoUpdateInfo> Updates,
    int TotalChecked
);

public record RepoUpdateInfo(
    string RepoUrl,
    string RepoName,
    string CurrentTag,
    string LatestTag,
    bool HasUpdate,
    List<string> AssetNames,
    string? Changelog
);

public record DownloadRequest(
    List<string> RepoUrls,
    bool DownloadAnyways = false
);

public record DownloadProgress(
    string RepoUrl,
    string RepoName,
    double ProgressPercent,
    string Status
);

public record DownloadResponse(
    bool Success,
    string Message,
    List<DownloadedAsset> DownloadedAssets
);

public record DownloadedAsset(
    string RepoUrl,
    string RepoName,
    string AssetName,
    string Tag,
    string LocalPath,
    bool Installed
);

public record InstallRequest(
    List<string> RepoUrls,
    bool DownloadAnyways = false,
    string? Password = null
);

public record RootStatusResponse(
    bool IsLinux,
    bool IsRoot,
    bool RequiresPassword
);

public record InstallResponse(
    bool Success,
    string Message,
    List<InstalledAsset> InstalledAssets
);

public record InstalledAsset(
    string RepoUrl,
    string RepoName,
    string AssetName,
    string Tag,
    string LocalPath
);

public record HealthResponse(
    string Status,
    string Version,
    int RepoCount,
    DateTime Timestamp
);

public record SetPatRequest(
    [Required] string Pat
);

public record PatStatusResponse(
    bool HasPat
);

public record SettingsInfoResponse(
    string Version,
    int RepoCount
);

public record ClearDataResponse(
    bool Success,
    string Message
);

public record ErrorResponse(
    string Error,
    string? Details = null
);

public record OperationLogEntry(
    DateTime Timestamp,
    string Message,
    string Level
);

public record OperationProgressResponse(
    bool IsRunning,
    string Title,
    string Status,
    double? Percent,
    bool Indeterminate,
    int CompletedItems,
    int TotalItems,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    bool? Success,
    string? Error,
    List<OperationLogEntry> Logs
);