using Github_Downloader_lib;

namespace Github_Downloader_cli.Args;

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
            bool success = await (install ?? LocalFileInstaller.InstallAsync)(args[1], output.WriteLine,
                output.WriteLine, args.Length == 4 ? args[3] : null);
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
