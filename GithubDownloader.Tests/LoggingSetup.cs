using GithubDownloader.Core.LocalApplications;
using GithubDownloader.Core.History;
using System.Runtime.CompilerServices;
using LoggerLib;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace GithubDownloader.Tests;

internal static class LoggingSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        TestStorage.Initialize();
        // Match production startup before any tests can invoke the global LoggerLib stream.
        Logger.LogFirstChance = false;
        Logger.LogDebug = false;
        Logger.LogLevel = LogLevel.Info;
        Logger.LogToTerminal = false;
        string directory = Path.Combine(Path.GetTempPath(), "ghd-test-logs-" + Guid.NewGuid().ToString("N"));
        Logger.LogDir = directory;
        GithubDownloader.Core.History.OperationHistory.Store = new(Path.Combine(directory, "history.json"));
        GithubDownloader.Core.LocalApplications.LocalApps.Store = new(Path.Combine(directory, "local-apps.json"));
        Task.Run(Logger.CreateFileAsync).GetAwaiter().GetResult();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        };
    }
}
