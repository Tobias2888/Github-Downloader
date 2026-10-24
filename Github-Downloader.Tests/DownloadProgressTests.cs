using System.Globalization;
using Github_Downloader_lib;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class DownloadProgressTests
{
    [Fact]
    public async Task CopyCancelsDuringTransferWithoutReportingCompletion()
    {
        using CancellationTokenSource cancellation = new();
        using MemoryStream source = new(new byte[250_000]);
        using MemoryStream destination = new();
        List<DownloadProgress> updates = [];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Api.CopyDownloadAsync(source, destination,
            source.Length, transferProgress: snapshot =>
            {
                updates.Add(snapshot);
                cancellation.Cancel();
            }, cancellationToken: cancellation.Token));
        Assert.Equal(0, destination.Length);
        Assert.DoesNotContain(updates, snapshot => snapshot.Percent == 100);
    }

    [Fact]
    public void SamplesAreThrottledAndSpeedAndEtaUseElapsedTime()
    {
        List<DownloadProgress> updates = [];
        DownloadProgressSampler sampler = new(500_000_000, updates.Add);
        sampler.Report(1_000, TimeSpan.FromMilliseconds(100));
        Assert.Empty(updates);
        sampler.Report(125_000_000, TimeSpan.FromSeconds(5));
        DownloadProgress first = Assert.Single(updates);
        Assert.Equal(25, first.Percent);
        Assert.Equal(25_000_000, first.BytesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(15), first.RemainingTime);
        sampler.Report(126_000_000, TimeSpan.FromSeconds(6));
        Assert.InRange(updates[1].BytesPerSecond, 1_000_001, 24_999_999);
        sampler.Report(500_000_000, TimeSpan.FromSeconds(6.1), completed: true);
        Assert.Equal(100, updates[^1].Percent);
        Assert.Equal(TimeSpan.Zero, updates[^1].RemainingTime);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void UnknownSizesStillReportBytesAndSpeed(long? total)
    {
        List<DownloadProgress> updates = [];
        DownloadProgressSampler sampler = new(total, updates.Add);
        sampler.Report(8_000_000, TimeSpan.FromSeconds(1));
        DownloadProgress update = Assert.Single(updates);
        Assert.Equal(8_000_000, update.DownloadedBytes);
        Assert.Equal(8_000_000, update.BytesPerSecond);
        Assert.Null(update.TotalBytes);
        Assert.Null(update.Percent);
        Assert.Null(update.RemainingTime);
        Assert.DoesNotContain("%", update.DisplayText);
    }

    [Theory]
    [InlineData(50L)]
    [InlineData(150L)]
    public void MismatchedLengthsDoNotShowCompletionPercentage(long received)
    {
        List<DownloadProgress> updates = [];
        DownloadProgressSampler sampler = new(100, updates.Add);
        sampler.Report(received, TimeSpan.FromSeconds(1), completed: true);
        Assert.Null(updates[0].TotalBytes);
        Assert.Null(updates[0].Percent);
        Assert.Null(updates[0].RemainingTime);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(250_000L)]
    public async Task CopyReportsInitialAndFinalBytesWithoutDependingOnSynchronizationContext(long? total)
    {
        byte[] bytes = new byte[250_000];
        Array.Fill<byte>(bytes, 42);
        using MemoryStream source = new(bytes);
        using MemoryStream destination = new();
        List<DownloadProgress> updates = [];
        await Api.CopyDownloadAsync(source, destination, total, transferProgress: updates.Add);
        Assert.Equal(bytes, destination.ToArray());
        Assert.Equal(0, updates[0].DownloadedBytes);
        Assert.Null(updates[0].RemainingTime);
        Assert.Equal(bytes.Length, updates[^1].DownloadedBytes);
        Assert.Equal(total.HasValue ? 100d : null, updates[^1].Percent);
    }

    [Fact]
    public void NewTransferDoesNotInheritPreviousRate()
    {
        List<DownloadProgress> updates = [];
        DownloadProgressSampler first = new(1000, updates.Add);
        first.Report(1000, TimeSpan.FromSeconds(1), completed: true);
        DownloadProgressSampler second = new(1000, updates.Add);
        second.Report(100, TimeSpan.FromSeconds(1));
        Assert.Equal(100, updates[^1].BytesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(9), updates[^1].RemainingTime);
    }

    [Theory]
    [InlineData("en-US", "125.0 MB / 500.0 MB (25.0%)")]
    [InlineData("de-AT", "125,0 MB / 500,0 MB (25,0%)")]
    public void ProgressTextUsesReadableUnitsAndCurrentCulture(string culture, string expected)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            DownloadProgress progress = new(125_000_000, 500_000_000, 8_200_000, TimeSpan.FromSeconds(45.8));
            Assert.Contains(expected, progress.DisplayText);
            Assert.Contains(culture == "de-AT" ? "8,2 MB/s" : "8.2 MB/s", progress.DisplayText);
            Assert.Contains("46 s remaining", progress.DisplayText);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
