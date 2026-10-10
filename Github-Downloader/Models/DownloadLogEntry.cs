using System;

namespace Github_Downloader.Models;

public class DownloadLogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Message { get; init; } = "";
    public string Level { get; init; } = "info";

    public string TimeText => Timestamp.ToString("HH:mm:ss");
    public bool IsError => Level == "error";
    public bool IsSuccess => Level == "success";
    public bool IsDetail => Level == "detail";
}
