using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Github_Downloader.Models;
using Github_Downloader_lib.Models;

namespace Github_Downloader.Services;

public class NotificationService
{
    private readonly Func<NotificationSettings> _settings;
    private readonly Func<string, Task<bool>> _send;
    private readonly Func<DateTime> _now;
    private readonly HashSet<(string Name, string Tag)> _announced = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NotificationService(Func<NotificationSettings> settings, Func<string, Task<bool>> send,
        Func<DateTime>? now = null)
    {
        _settings = settings;
        _send = send;
        _now = now ?? (() => DateTime.Now);
    }

    public async Task NotifyUpdatesAvailableAsync(IEnumerable<Repo> repositories)
    {
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            HashSet<(string Name, string Tag)> available = repositories
                .Where(repo => !repo.ExcludedFromDownloadAll && !repo.IsUpToDate && !string.IsNullOrEmpty(repo.Tag))
                .Select(repo => (repo.Name, repo.Tag)).ToHashSet();
            _announced.IntersectWith(available);

            NotificationSettings settings = _settings();
            if (!settings.UpdatesAvailable || !settings.AllowsNotifications(_now())
                || !available.Except(_announced).Any()) return;

            string message = available.Count == 1 ? "1 update is available" : $"{available.Count} updates are available";
            if (await _send(message)) _announced.UnionWith(available);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task NotifyDownloadsCompletedAsync(int count, string? repositoryName = null)
    {
        NotificationSettings settings = _settings();
        if (count <= 0 || !settings.DownloadsCompleted || !settings.AllowsNotifications(_now())) return;

        string message = count == 1
            ? $"Update completed for {repositoryName ?? "1 repository"}"
            : $"Updates completed for {count} repositories";
        await _send(message);
    }
}
