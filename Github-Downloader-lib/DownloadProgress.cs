using System.Globalization;

namespace Github_Downloader_lib;

/// <summary>A snapshot of a single file transfer. Unknown totals and estimates remain null.</summary>
public sealed record DownloadProgress(long DownloadedBytes, long? TotalBytes, double BytesPerSecond,
    TimeSpan? RemainingTime)
{
    public double? Percent => TotalBytes is > 0 && DownloadedBytes <= TotalBytes.Value
        ? DownloadedBytes * 100.0 / TotalBytes.Value : null;

    public string DisplayText
    {
        get
        {
            string size = TotalBytes is > 0
                ? $"{FormatBytes(DownloadedBytes)} / {FormatBytes(TotalBytes.Value)}"
                : $"{FormatBytes(DownloadedBytes)} / unknown size";
            string percent = Percent is double value ? $" ({value:0.0}%)" : "";
            string speed = BytesPerSecond > 0 ? $"{FormatBytes(BytesPerSecond)}/s" : "Speed unavailable";
            string remaining = RemainingTime is TimeSpan time
                ? $"{Math.Ceiling(time.TotalSeconds):0} s remaining" : "Time remaining unknown";
            return $"{size}{percent} · {speed} · {remaining}";
        }
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unit = 0;
        while (bytes >= 1000 && unit < units.Length - 1)
        {
            bytes /= 1000;
            unit++;
        }
        return bytes.ToString(unit == 0 ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + units[unit];
    }
}

/// <summary>Samples transfers at most four times a second using a smoothed transfer rate.</summary>
internal sealed class DownloadProgressSampler(long? totalBytes, Action<DownloadProgress> report)
{
    private TimeSpan _lastElapsed;
    private long _lastBytes;
    private double _speed;
    private bool _totalReliable = totalBytes is > 0;

    public void Report(long downloadedBytes, TimeSpan elapsed, bool completed = false)
    {
        if (totalBytes.HasValue && downloadedBytes > totalBytes.Value) _totalReliable = false;
        double seconds = (elapsed - _lastElapsed).TotalSeconds;
        if (!completed && seconds < 0.25) return;
        if (seconds > 0)
        {
            double rate = (downloadedBytes - _lastBytes) / seconds;
            double weight = 1 - Math.Exp(-seconds / 2);
            _speed = _speed > 0 ? _speed + weight * (rate - _speed) : rate;
        }
        _lastBytes = downloadedBytes;
        _lastElapsed = elapsed;
        // A short or oversized response cannot provide a trustworthy percentage or ETA.
        if (completed && totalBytes != downloadedBytes) _totalReliable = false;
        long? total = _totalReliable ? totalBytes : null;
        double? remainingSeconds = total.HasValue && _speed > 0
            ? (total.Value - downloadedBytes) / _speed : null;
        TimeSpan? remaining = remainingSeconds is >= 0 && remainingSeconds < TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(remainingSeconds.Value) : null;
        report(new DownloadProgress(downloadedBytes, total, _speed, remaining));
    }
}
