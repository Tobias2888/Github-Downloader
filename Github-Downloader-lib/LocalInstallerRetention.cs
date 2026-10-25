using System.Text.Json;

namespace Github_Downloader_lib;

public static class LocalInstallerRetention
{
    public const int DefaultVersionsToKeep = 2;

    // A null limit means unlimited. Read the shared setting so CLI installs follow it too.
    public static int? LoadLimit() => LoadLimit(Path.Combine(FileManager.AppdataPath, "appsettings.json"));

    internal static int? LoadLimit(string path)
    {
        if (!File.Exists(path)) return DefaultVersionsToKeep;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("LocalInstallerVersionsToKeep", out JsonElement value))
            return DefaultVersionsToKeep;
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int limit) || limit < 0)
            throw new InvalidDataException("The saved local installer version limit is invalid.");
        return limit;
    }
}
