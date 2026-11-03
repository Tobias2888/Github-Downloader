using System.Diagnostics;
using Generated;

namespace GithubDownloader.Core;

public static class AppInfo
{
    public const string Version = BuildInfo.Version;
    public static readonly string CliName;

    static AppInfo()
    {
        CliName = Process.GetCurrentProcess().ProcessName;
    }
}