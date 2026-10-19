using System.Globalization;
using System.Text.Json;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;

namespace Github_Downloader_cli.Args;

internal static class ArgHistory
{
    internal static int Execute(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length > 1 && args[1] == "clear")
            {
                if (args.Length != 3 || args[2] != "--yes")
                    throw new ArgumentException("Use history clear --yes to delete completed history. Running entries are kept.");
                output.WriteLine(JsonSerializer.Serialize(new { removed = OperationHistory.Store.Clear() }));
                return 0;
            }
            string? repository = null;
            DateTimeOffset? from = null;
            DateTimeOffset? to = null;
            HistoryStatus? status = null;
            HistoryAction? action = null;
            Guid? batch = null;
            int offset = 0;
            int limit = 200;
            bool json = false;
            bool steps = false;
            bool includeSkipped = false;
            for (int index = 1; index < args.Length; index++)
            {
                string option = args[index];
                if (option == "--json") { json = true; continue; }
                if (option == "--include-skipped") { includeSkipped = true; continue; }
                if (option == "--steps") { steps = true; continue; }
                if (++index >= args.Length) throw new ArgumentException("A history filter is missing its value.");
                string value = args[index];
                switch (option)
                {
                    case "--repo": repository = value; break;
                    case "--from": from = ParseDate(value); break;
                    case "--to": to = ParseDate(value); break;
                    case "--status": status = ParseEnum<HistoryStatus>(value); break;
                    case "--action": action = ParseEnum<HistoryAction>(value); break;
                    case "--batch": batch = Guid.Parse(value); break;
                    case "--offset": offset = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--limit": limit = int.Parse(value, CultureInfo.InvariantCulture); break;
                    default: throw new ArgumentException("Unknown history option.");
                }
            }
            HistoryPage page = OperationHistory.Store.Query(new(repository, from, to, status, action, batch, offset, limit, steps, includeSkipped));
            if (json) output.WriteLine(JsonSerializer.Serialize(page, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true
            }));
            else
            {
                foreach (HistoryEntry entry in page.Entries)
                    output.WriteLine($"{entry.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} | " +
                        $"{entry.DisplayName} | {entry.Action} | " +
                        $"{entry.FromVersion} → {entry.ToVersion} | {entry.Status} | {entry.Summary}");
                output.WriteLine($"Showing {page.Entries.Count} of {page.Total} entries.");
            }
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            error.WriteLine("Invalid history arguments. Use --repo, --from, --to, --status, --action, --batch, --offset, --limit, --steps, --include-skipped, --json; or history clear --yes.");
            return 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine("History could not be read or saved. Check file access; existing history was preserved.");
            return 1;
        }
    }

    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value,
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out T parsed) && Enum.IsDefined(parsed)
            ? parsed : throw new ArgumentException("Unknown history filter.");
}
