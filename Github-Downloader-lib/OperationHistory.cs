using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Github_Downloader_lib.Models;

namespace Github_Downloader_lib;

public static class OperationHistory
{
    public static HistoryStore Store { get; set; } = new(Path.Combine(FileManager.AppdataPath, "history.json"));
}

public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly long ProcessStart = GetProcessStart(Environment.ProcessId);
    private readonly string _path;
    private readonly string _mutexName;

    public HistoryStore(string path)
    {
        _path = Path.GetFullPath(path);
        _mutexName = "ghd-history-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_path)));
    }

    public static string RepositoryKey(Repo repo) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(repo.Url.TrimEnd('/').ToUpperInvariant())));

    public HistoryScope Begin(HistoryAction action, Repo? repo = null, Guid? parentId = null,
        bool isBatch = false, Guid? batchId = null, string? toVersion = null, string? localFileName = null)
    {
        HistoryEntry entry = new()
        {
            Id = Guid.NewGuid(), ParentId = parentId, BatchId = batchId, IsBatch = isBatch,
            RepositoryKey = repo is null || localFileName is not null ? "" : RepositoryKey(repo),
            RepositoryName = repo is null || localFileName is not null ? "" : SafeName(repo.Name),
            LocalFileName = localFileName is null ? "" : SafeFileName(localFileName),
            FromVersion = repo is null ? "" : SafeVersion(action switch
            {
                HistoryAction.Download => repo.LastDownloadTag,
                HistoryAction.Uninstall => repo.CurrentInstallTag,
                _ => repo.CurrentVersion
            }),
            ToVersion = SafeVersion(toVersion ?? (action == HistoryAction.Uninstall ? "" : repo?.Tag ?? "")),
            StartedAt = DateTimeOffset.UtcNow, Action = action,
            Status = HistoryStatus.Running, Outcome = HistoryOutcome.Started
        };
        if (isBatch) entry = entry with { BatchId = entry.Id };
        Locked(entries =>
        {
            entries.Add(new StoredEntry(entry, Environment.ProcessId, ProcessStart));
            Save(entries);
            return true;
        });
        return new HistoryScope(this, entry.Id);
    }

    public HistoryPage Query(HistoryQuery? query = null)
    {
        query ??= new();
        if (query.Offset < 0 || query.Limit is < 1 or > 1000 || query.From > query.To)
            throw new ArgumentException("Choose a valid time range, offset, and limit (1–1000).");
        return Locked(entries =>
        {
            IEnumerable<HistoryEntry> filtered = entries.Select(stored => stored.Entry);
            if (!query.IncludeSkipped && query.Status != HistoryStatus.Skipped)
                filtered = filtered.Where(entry => entry.Status != HistoryStatus.Skipped
                    && !(entry.IsBatch && entry.Outcome == HistoryOutcome.BatchCompleted
                        && entry.SucceededCount == 0 && entry.FailedCount == 0));
            if (!query.IncludeSteps)
            {
                HashSet<Guid> batches = entries.Where(stored => stored.Entry.IsBatch).Select(stored => stored.Entry.Id).ToHashSet();
                filtered = filtered.Where(entry => !entry.ParentId.HasValue || batches.Contains(entry.ParentId.Value));
            }
            if (!string.IsNullOrWhiteSpace(query.Repository))
                filtered = filtered.Where(entry => entry.DisplayName.Contains(query.Repository, StringComparison.OrdinalIgnoreCase)
                    || entry.RepositoryKey.Equals(query.Repository, StringComparison.OrdinalIgnoreCase));
            if (query.From.HasValue) filtered = filtered.Where(entry => entry.StartedAt >= query.From.Value);
            if (query.To.HasValue) filtered = filtered.Where(entry => entry.StartedAt < query.To.Value);
            if (query.Status.HasValue) filtered = filtered.Where(entry => entry.Status == query.Status.Value);
            if (query.Action.HasValue) filtered = filtered.Where(entry => entry.Action == query.Action.Value);
            if (query.BatchId.HasValue) filtered = filtered.Where(entry => entry.BatchId == query.BatchId.Value);
            HistoryEntry[] ordered = filtered.OrderByDescending(entry => entry.StartedAt).ToArray();
            return new HistoryPage(ordered.Skip(query.Offset).Take(query.Limit).ToArray(), ordered.Length, query.Offset, query.Limit);
        });
    }

    // Running operations stay journaled so clearing the view cannot hide an unfinished installation.
    public int Clear()
    {
        return Locked(entries =>
        {
            int count = entries.RemoveAll(stored => stored.Entry.Status != HistoryStatus.Running);
            if (count > 0) Save(entries);
            return count;
        });
    }

    internal void Finish(Guid id, HistoryStatus status, HistoryOutcome outcome,
        int succeeded = 0, int failed = 0, int skipped = 0)
    {
        Locked(entries =>
        {
            int index = entries.FindIndex(stored => stored.Entry.Id == id);
            if (index < 0 || entries[index].Entry.Status != HistoryStatus.Running) return false;
            StoredEntry stored = entries[index];
            entries[index] = stored with { Entry = stored.Entry with
            {
                FinishedAt = DateTimeOffset.UtcNow, Status = status, Outcome = outcome,
                SucceededCount = succeeded, FailedCount = failed, SkippedCount = skipped
            }};
            Save(entries);
            return true;
        });
    }

    private T Locked<T>(Func<List<StoredEntry>, T> operation)
    {
        using Mutex mutex = new(false, _mutexName);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }
        try
        {
            List<StoredEntry> entries;
            try
            {
                entries = File.Exists(_path)
                    ? JsonSerializer.Deserialize<List<StoredEntry>>(File.ReadAllText(_path), JsonOptions)
                        ?? throw new JsonException()
                    : [];
            }
            catch (JsonException)
            {
                throw new InvalidDataException("History could not be read. The existing file was preserved.");
            }
            if (entries.Any(stored => stored is null || stored.Entry is null || stored.Entry.Id == Guid.Empty
                || stored.Entry.RepositoryName is null || stored.Entry.RepositoryKey is null
                || stored.Entry.LocalFileName is null || stored.Entry.FromVersion is null || stored.Entry.ToVersion is null
                || !Enum.IsDefined(stored.Entry.Action) || !Enum.IsDefined(stored.Entry.Status)
                || !Enum.IsDefined(stored.Entry.Outcome)))
                throw new InvalidDataException("History could not be read. The existing file was preserved.");
            bool recovered = false;
            foreach (StoredEntry stored in entries.ToArray())
            {
                if (stored.Entry.Status != HistoryStatus.Running) continue;
                long observedStart = GetProcessStart(stored.ProcessId);
                if (observedStart == stored.ProcessStart || observedStart == -2) continue;
                int index = entries.IndexOf(stored);
                entries[index] = stored with { Entry = stored.Entry with
                {
                    Status = HistoryStatus.Interrupted, Outcome = HistoryOutcome.Interrupted,
                    FinishedAt = DateTimeOffset.UtcNow
                }};
                recovered = true;
            }
            if (recovered) Save(entries);
            return operation(entries);
        }
        finally { mutex.ReleaseMutex(); }
    }

    private void Save(List<StoredEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream file = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                JsonSerializer.Serialize(file, entries, JsonOptions);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static long GetProcessStart(int id)
    {
        try
        {
            using Process process = Process.GetProcessById(id);
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return -1; }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or NotSupportedException) { return -2; }
    }

    // History contains identifiers and fixed outcomes, never request URLs or exception/installer output.
    private static string SafeName(string value) => Regex.IsMatch(value, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z")
        && value.Length <= 256 ? value : "Repository";
    private static string SafeFileName(string value)
    {
        string name = Regex.Replace(Path.GetFileName(value), @"[\p{Cc}\p{Cf}]", "");
        return name[..Math.Min(256, name.Length)];
    }

    private static string SafeVersion(string value) => value.Length <= 128
        && Regex.IsMatch(value, @"\A[A-Za-z0-9._/+\-]*\z") ? value : "[redacted]";

    internal sealed record StoredEntry(HistoryEntry Entry, int ProcessId, long ProcessStart);
}

public sealed class HistoryScope : IDisposable
{
    private readonly HistoryStore _store;
    private bool _finished;
    public Guid Id { get; }
    internal HistoryScope(HistoryStore store, Guid id) { _store = store; Id = id; }

    public void Complete(HistoryOutcome outcome = HistoryOutcome.Completed) => Finish(HistoryStatus.Succeeded, outcome);
    public void Finish(HistoryStatus status, HistoryOutcome outcome, int succeeded = 0, int failed = 0, int skipped = 0)
    {
        if (_finished) return;
        _store.Finish(Id, status, outcome, succeeded, failed, skipped);
        _finished = true;
    }
    public void Dispose()
    {
        if (!_finished) Finish(HistoryStatus.Failed, HistoryOutcome.Failed);
    }
}
