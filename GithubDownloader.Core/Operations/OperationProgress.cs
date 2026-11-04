using GithubDownloader.Core.Downloads;
namespace GithubDownloader.Core.Operations;

public enum OperationPhase { Download, Extraction }

/// <summary>Measurements are independent of the text shown by each frontend.</summary>
public sealed record OperationProgress(OperationPhase Phase, double? Percent, DownloadProgress? Transfer = null, bool StartsItem = false)
{
    public string DisplayText => Transfer?.DisplayText ?? (Percent.HasValue
        ? $"{(Phase == OperationPhase.Extraction ? "Extracting" : "Downloaded")}: {Percent:0.##}%"
        : Phase == OperationPhase.Extraction ? "Extracting..." : "Downloading...");
    public static OperationProgress Download(DownloadProgress transfer) => new(OperationPhase.Download, transfer.Percent, transfer);
}
