using System.Text.Json;
using Github_Downloader.ViewModels;
using Github_Downloader_Web.DTOs;
using Github_Downloader_Web.Services;
using Xunit;
using TransferProgress = Github_Downloader_lib.DownloadProgress;

namespace Github_Downloader.Tests;

public sealed class WebTransferProgressTests
{
    [Fact]
    public void WebSnapshotExposesMeasurementsAndResetsBetweenPhasesAndOperations()
    {
        OperationTracker tracker = new();
        tracker.Start("Download", 2);
        tracker.StartItem();
        TransferProgress transfer = new(125_000_000, 500_000_000, 8_200_000, TimeSpan.FromSeconds(46));
        tracker.SetTransferProgress(transfer);
        OperationProgressResponse snapshot = tracker.Snapshot();
        Assert.Equal(transfer, snapshot.Transfer);
        Assert.Equal(12.5, snapshot.Percent);
        Assert.DoesNotContain(snapshot.Logs, entry => entry.Message.Contains("MB/s"));
        string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"downloadedBytes\":125000000", json);
        Assert.Contains("\"displayText\":", json);

        tracker.Log("Extracting archive");
        Assert.Null(tracker.Snapshot().Transfer);
        tracker.StartItem();
        tracker.SetTransferProgress(new TransferProgress(1000, null, 500, null));
        Assert.True(tracker.Snapshot().Indeterminate);
        Assert.Null(tracker.Snapshot().Percent);
        tracker.Finish(false);
        Assert.Null(tracker.Snapshot().Transfer);
        tracker.Start("Retry", 1);
        Assert.Null(tracker.Snapshot().Transfer);
        Assert.Null(tracker.Snapshot().Percent);
    }
}

[Collection("Repository dragging")]
public sealed class DesktopTransferProgressTests(RepositoryDragApplication application)
{
    [Fact]
    public async Task DesktopDisplaysMeasurementsWithoutAddingLogsAndClearsBetweenFiles()
    {
        await application.Dispatch(() =>
        {
            DownloadStatusViewModel model = new();
            model.LogStatus("Downloading first file");
            TransferProgress transfer = new(125_000_000, 500_000_000, 8_200_000, TimeSpan.FromSeconds(46));
            model.ReportTransfer(transfer);
            Assert.Equal(25, model.Percent);
            Assert.Equal(transfer.DisplayText, model.ProgressText);
            Assert.Single(model.Logs);
            model.LogStatus("Extracting first file");
            Assert.Equal("", model.ProgressText);
            Assert.True(model.IsIndeterminate);
            model.LogStatus("Downloading second file");
            model.ReportTransfer(new TransferProgress(1000, null, 500, null));
            Assert.True(model.IsIndeterminate);
            Assert.Contains("unknown size", model.ProgressText);
            return Task.CompletedTask;
        }, CancellationToken.None);
    }
}
