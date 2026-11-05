using System.Diagnostics;
using System.Reflection;

namespace GithubDownloader.Core;

public static class AppInfo
{
    public static readonly string Version = typeof(AppInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    public static readonly string CliName;

    static AppInfo()
    {
        CliName = Process.GetCurrentProcess().ProcessName;
    }
}
