using System.Runtime.CompilerServices;
using LoggerLib;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Github_Downloader.Tests;

internal static class LoggingSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Match production startup before any tests can invoke the global LoggerLib stream.
        Logger.LogFirstChance = false;
        Logger.LogDebug = false;
        Logger.LogLevel = LogLevel.Info;
        Logger.LogToTerminal = false;
        string directory = Path.Combine(Path.GetTempPath(), "ghd-test-logs-" + Guid.NewGuid().ToString("N"));
        Logger.LogDir = directory;
        Github_Downloader_lib.OperationHistory.Store = new(Path.Combine(directory, "history.json"));
        Task.Run(Logger.CreateFile).GetAwaiter().GetResult();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        };
    }
}
