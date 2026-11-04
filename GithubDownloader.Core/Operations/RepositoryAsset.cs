using GithubDownloader.Core.Models;

namespace GithubDownloader.Core.Operations;

internal readonly record struct RepositoryAsset(Repo Repo, string TempAssetPath, string Tag, string? TemporaryDirectory = null,
        Guid? ParentHistoryId = null, Guid? BatchId = null, string? LocalSourcePath = null,
        string? ExtractionDirectory = null, Action<string, bool>? ExtractionFileWritten = null);
