using System.Text.Json.Serialization;

namespace Github_Downloader_lib.Models;

[JsonConverter(typeof(JsonStringEnumConverter<HistoryAction>))]
public enum HistoryAction { Update, Download, Save, Install, Extract, Reinstall, Uninstall }

[JsonConverter(typeof(JsonStringEnumConverter<HistoryStatus>))]
public enum HistoryStatus { Running, Succeeded, Failed, PartialFailure, Skipped, Interrupted }

[JsonConverter(typeof(JsonStringEnumConverter<HistoryOutcome>))]
public enum HistoryOutcome
{
    Started, Completed, Downloaded, Saved, Installed, Extracted, Uninstalled,
    SelectionUnresolved, DownloadFailed, SaveFailed, InstallFailed, ExtractionFailed,
    UninstallFailed, AlreadyCurrent, Excluded, Interrupted, Failed, BatchCompleted, InstallerLaunched
}

public sealed record HistoryEntry
{
    public Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public Guid? BatchId { get; init; }
    public bool IsBatch { get; init; }
    public string RepositoryKey { get; init; } = "";
    public string RepositoryName { get; init; } = "";
    public string LocalFileName { get; init; } = "";
    public string DisplayName => IsBatch ? "Batch" : LocalFileName.Length > 0 ? $"Local: {LocalFileName}" : RepositoryName;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public HistoryAction Action { get; init; }
    public string FromVersion { get; init; } = "";
    public string ToVersion { get; init; } = "";
    public HistoryStatus Status { get; init; }
    public HistoryOutcome Outcome { get; init; }
    public int SucceededCount { get; init; }
    public int FailedCount { get; init; }
    public int SkippedCount { get; init; }

    public string Summary => IsBatch && Outcome == HistoryOutcome.BatchCompleted
        ? $"{SucceededCount} succeeded, {FailedCount} failed."
        : Outcome switch
        {
            HistoryOutcome.Started => "In progress.",
            HistoryOutcome.Downloaded => "Download completed; installation has not been confirmed.",
            HistoryOutcome.Saved => "File saved. No installation performed.",
            HistoryOutcome.Installed => "Installation completed.",
            HistoryOutcome.InstallerLaunched => "Self-update installer started; installation has not been confirmed. Setup offers to reopen the app on its finish page.",
            HistoryOutcome.Extracted => "Archive extracted successfully.",
            HistoryOutcome.Uninstalled => "Uninstall completed.",
            HistoryOutcome.SelectionUnresolved => "No unique asset selected. Download blocked.",
            HistoryOutcome.DownloadFailed => "Download failed. Check connectivity and access.",
            HistoryOutcome.SaveFailed => "Could not save the downloaded file.",
            HistoryOutcome.InstallFailed => "Installation failed. Check permissions and installer requirements.",
            HistoryOutcome.ExtractionFailed => "Archive extraction failed.",
            HistoryOutcome.UninstallFailed => "Uninstall failed. Check permissions and installation requirements.",
            HistoryOutcome.AlreadyCurrent => "Already up to date; no action taken.",
            HistoryOutcome.Excluded => "Excluded from the batch; no action taken.",
            HistoryOutcome.Interrupted => "Interrupted before completion could be confirmed.",
            HistoryOutcome.Failed => "Operation did not complete.",
            _ => "Operation completed."
        };
}

public sealed record HistoryQuery(string? Repository = null, DateTimeOffset? From = null,
    DateTimeOffset? To = null, HistoryStatus? Status = null, HistoryAction? Action = null,
    Guid? BatchId = null, int Offset = 0, int Limit = 200, bool IncludeSteps = true, bool IncludeSkipped = false);

public sealed record HistoryPage(IReadOnlyList<HistoryEntry> Entries, int Total, int Offset, int Limit);
