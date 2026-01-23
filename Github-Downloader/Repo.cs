namespace Github_Downloader;

public class Repo
{
    public required string Url { get; set; }
    public required string DownloadUrl { get; set; }
    public string CurrentName { get; set; } = "";
}