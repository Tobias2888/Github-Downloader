using GithubDownloader.Core.LocalApplications;
using GithubDownloader.Core;
using GithubDownloader.Core.Notifications;

namespace GithubDownloader.Cli.Args;

internal static class ArgInstallLocal
{
    internal static async Task<int> ExecuteAsync(string[] args, TextWriter output, TextWriter error,
        Func<string, Action<string>, Action<string>, string?, Task<bool>>? install = null)
    {
        if (args.Length != 2 && (args.Length != 4 || args[2] != "--extract-to"))
        {
            error.WriteLine("Usage: ghd install-local <file> [--extract-to <folder>]");
            return 2;
        }
        try
        {
            bool success = install is not null
                ? await install(args[1], output.WriteLine, output.WriteLine, args.Length == 4 ? args[3] : null)
                : await KdeOperationNotification.RunAsync("Installing local application",
                    (status, transfer, token) => LocalFileInstaller.InstallAsync(args[1], status,
                        status, args.Length == 4 ? args[3] : null, operationProgress: transfer), output.WriteLine, cancellable: false);
            output.WriteLine(success ? "Local file installed." : "Local file installation failed.");
            return success ? 0 : 1;
        }
        catch (ArgumentException exception)
        {
            error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine("The local file could not be installed. Check file access and installation requirements.");
            return 1;
        }
    }
}
