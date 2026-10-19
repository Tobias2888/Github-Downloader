using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Github_Downloader_lib.Models;

namespace Github_Downloader_lib;

public static class LocalApps
{
    public static LocalAppStore Store { get; set; } = new(Path.Combine(FileManager.AppdataPath, "local-apps.json"));
}

public sealed class LocalAppStore
{
    private readonly string _path;
    private readonly string _mutexName;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public LocalAppStore(string path)
    {
        _path = Path.GetFullPath(path);
        _mutexName = "ghd-local-apps-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_path)));
    }

    public IReadOnlyList<LocalApp> Read() => Locked(apps => apps.OrderByDescending(app => app.InstalledAt).ToArray(), false);

    public void Save(LocalApp app) => Locked(apps =>
    {
        apps.RemoveAll(existing => existing.Id == app.Id);
        apps.Add(app);
        return true;
    }, true);

    public void Remove(string id) => Locked(apps => apps.RemoveAll(app => app.Id == id), true);

    private T Locked<T>(Func<List<LocalApp>, T> action, bool save)
    {
        using Mutex mutex = new(false, _mutexName);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }
        try
        {
            List<LocalApp> apps;
            try
            {
                apps = File.Exists(_path) ? JsonSerializer.Deserialize<List<LocalApp>>(File.ReadAllText(_path), Options)
                    ?? throw new JsonException() : [];
                if (apps.Any(app => app is null || string.IsNullOrWhiteSpace(app.Id) || string.IsNullOrWhiteSpace(app.FileName)
                    || string.IsNullOrWhiteSpace(app.InstallationName) || !Enum.IsDefined(app.Kind)
                    || app.DebianPackage is null || app.AppImageId is null || app.ExtractionDirectory is null || app.OwnedFiles is null
                    || app.OwnedFiles.Any(file => string.IsNullOrWhiteSpace(file.Key) || string.IsNullOrWhiteSpace(file.Value))))
                    throw new JsonException();
            }
            catch (JsonException) { throw new InvalidDataException("The local app list could not be read. Its file was preserved."); }
            T result = action(apps);
            if (save)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (FileStream file = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                        JsonSerializer.Serialize(file, apps, Options);
                        file.Flush(true);
                    }
                    File.Move(temporary, _path, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return result;
        }
        finally { mutex.ReleaseMutex(); }
    }
}
