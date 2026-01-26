using System.Collections.Generic;
using FileLib;

namespace Github_Downloader;

public class Repo
{
    public required string Url { get; set; }
    public required string Name { get; set; }
    public int DownloadAssetIndex { get; set; }
    public required List<string> AssetNames { get; set; }
    public required List<string> DownloadUrls { get; set; }
    public required string Tag { get; set; }
    public string CurrentInstallTag { get; set; } = "";
    public string DownloadPath { get; set; } = DirectoryHelper.GetUserDirPath();
}