using System.Collections.Generic;
using System.IO;
using FileLib;

namespace Github_Downloader;

public class Repo
{
    public required string Url { get; set; }
    public required string Name { get; set; }
    public int DownloadAssetIndex { get; set; } = 0;
    public required List<string> AssetNames { get; set; }
    public string CurrentName { get; set; } = "";
    public string DownloadPath { get; set; } = DirectoryHelper.GetUserDirPath();
}