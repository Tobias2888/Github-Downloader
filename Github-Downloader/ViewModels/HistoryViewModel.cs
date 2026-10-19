using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Github_Downloader_lib.Models;

namespace Github_Downloader.ViewModels;

public sealed class HistoryViewModel : ViewModelBase
{
    public ObservableCollection<HistoryRow> Entries { get; } = [];
}

public sealed record HistoryRow(HistoryEntry Entry)
{
    public string Timestamp => Entry.StartedAt.ToLocalTime().ToString("g");
    public string Repository => Entry.IsBatch ? "Batch" : Entry.RepositoryName;
    public string Action => (Entry.IsBatch ? "Batch " : "") + Entry.Action;
    public string Versions => $"{(Entry.FromVersion.Length > 0 ? Entry.FromVersion : "—")} → {(Entry.ToVersion.Length > 0 ? Entry.ToVersion : "—")}";
    public string Result => Entry.Status == HistoryStatus.PartialFailure ? "Partial failure" : Entry.Status.ToString();
    public IBrush ResultBrush => Entry.Status is HistoryStatus.Failed or HistoryStatus.PartialFailure or HistoryStatus.Interrupted
        ? Brushes.LightCoral : Entry.Status == HistoryStatus.Succeeded ? Brushes.LightGreen : Brushes.LightBlue;
    public string Summary => Entry.Summary;
    public string Group => Entry.BatchId.HasValue ? $"Batch: {Entry.BatchId}" : $"Operation: {Entry.Id}";
}
