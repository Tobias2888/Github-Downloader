namespace Github_Downloader;

public class Repo
{
    public required string Url { get; set; }
    public required string DownloadUrl { get; set; }
    public required string Name { get; set; }
    public int DownloadAssetIndex { get; set; } = 0;
    public string CurrentName { get; set; } = "";
}