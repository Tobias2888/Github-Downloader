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
        int logCount = tracker.Snapshot().Logs.Count;
        tracker.SetItemPercent(35, "Extracting: 35%");
        Assert.Equal("Extracting: 35%", tracker.Snapshot().Status);
        Assert.Equal(17.5, tracker.Snapshot().Percent);
        Assert.Equal(logCount, tracker.Snapshot().Logs.Count);
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
    public async Task ExtractionPercentagesUpdateTheBarWithoutAddingLogRows()
    {
        await application.Dispatch(async () =>
        {
            DownloadStatusViewModel model = new();
            await model.RunAsync(async () =>
            {
                model.LogStatus("Extracting application");
                await Task.Run(() =>
                {
                    for (int percent = 0; percent <= 100; percent++) model.LogProgress($"Extracting: {percent}%");
                });
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.Equal(100, model.Percent);
                Assert.Equal("Extracting: 100%", model.ProgressText);
                Assert.Equal("Extracting application", model.StatusText);
                Assert.Single(model.Logs);
                model.LogStatus("Installing next application");
                Assert.Null(model.Percent);
                Assert.True(model.IsIndeterminate);
                return true;
            });
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OperationFailuresShowTheSpecificReason()
    {
        await application.Dispatch(async () =>
        {
            DownloadStatusViewModel model = new();
            string reason = "The application-menu entry was changed or belongs to another application.";
            Assert.False(await model.RunAsync(() => Task.FromException<bool>(new IOException(reason))));
            Assert.Contains(model.Logs, entry => entry.Level == "error" && entry.Message.Contains(reason));
            Assert.False(model.IsUpdating);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CancelStopsOperationWithoutFailureAndAllowsAnotherDownload()
    {
        await application.Dispatch(async () =>
        {
            DownloadStatusViewModel model = new();
            Task<bool> running = model.RunAsync(async cancellationToken =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return true;
            });
            Assert.True(model.CanCancel);
            Assert.True(model.IsIndeterminate);
            model.Cancel();
            Assert.False(model.CanCancel);
            Assert.False(await running);
            Assert.False(model.IsUpdating);
            Assert.False(model.IsIndeterminate);
            Assert.Equal("Downloads cancelled.", model.StatusText);
            Assert.DoesNotContain(model.Logs, entry => entry.Level == "error");
            Assert.True(await model.RunAsync(_ => Task.FromResult(true)));
            Assert.False(model.CanCancel);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DesktopDisplaysMeasurementsWithoutAddingLogsAndClearsBetweenFiles()
    {
        await application.Dispatch(() =>
        {
            DownloadStatusViewModel model = new();
            return model.RunAsync(() =>
            {
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
                return Task.FromResult(true);
            });
        }, CancellationToken.None);
    }
}
