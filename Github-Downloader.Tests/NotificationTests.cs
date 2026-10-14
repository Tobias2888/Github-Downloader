using System.Text.Json;
using Github_Downloader.Models;
using Github_Downloader.Services;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

public class NotificationTests
{
    [Theory]
    [InlineData(8, 22, 7, 59, false)]
    [InlineData(8, 22, 8, 0, true)]
    [InlineData(8, 22, 21, 59, true)]
    [InlineData(8, 22, 22, 0, false)]
    [InlineData(22, 7, 22, 0, true)]
    [InlineData(22, 7, 0, 0, true)]
    [InlineData(22, 7, 6, 59, true)]
    [InlineData(22, 7, 7, 0, false)]
    [InlineData(22, 7, 12, 0, false)]
    [InlineData(8, 8, 3, 0, true)]
    public void DailyWindowUsesLocalTimeIncludingOvernight(int start, int end, int hour, int minute, bool expected)
    {
        NotificationSettings settings = new()
        {
            LimitToTimeWindow = true,
            StartTime = TimeSpan.FromHours(start),
            EndTime = TimeSpan.FromHours(end)
        };
        Assert.Equal(expected, settings.AllowsNotifications(new DateTime(2026, 10, 13, hour, minute, 0)));
    }

    [Fact]
    public void MasterSwitchOverridesAllDayAndScheduledNotifications()
    {
        NotificationSettings settings = new() { Enabled = false };
        Assert.False(settings.AllowsNotifications(DateTime.Now));
        settings.LimitToTimeWindow = true;
        Assert.False(settings.AllowsNotifications(DateTime.Now));
        settings.Enabled = true;
        settings.StartTime = TimeSpan.FromHours(-1);
        Assert.False(settings.AllowsNotifications(DateTime.Now));
        settings.LimitToTimeWindow = false;
        Assert.True(settings.AllowsNotifications(DateTime.Now));
    }

    [Fact]
    public void SettingsRoundTripAndOldFilesGetDefaults()
    {
        AppSettings oldSettings = JsonSerializer.Deserialize<AppSettings>("{\"AutoCheckForUpdates\":false}")!;
        Assert.True(oldSettings.Notifications.Enabled);
        Assert.False(oldSettings.Notifications.LimitToTimeWindow);
        oldSettings.Notifications = new()
        {
            Enabled = false, UpdatesAvailable = false, DownloadsCompleted = false,
            LimitToTimeWindow = true, StartTime = TimeSpan.FromHours(23), EndTime = TimeSpan.FromHours(6)
        };
        AppSettings reloaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(oldSettings))!;
        Assert.False(reloaded.Notifications.Enabled);
        Assert.False(reloaded.Notifications.UpdatesAvailable);
        Assert.False(reloaded.Notifications.DownloadsCompleted);
        Assert.True(reloaded.Notifications.LimitToTimeWindow);
        Assert.Equal(TimeSpan.FromHours(23), reloaded.Notifications.StartTime);
        Assert.Equal(TimeSpan.FromHours(6), reloaded.Notifications.EndTime);
    }

    [Fact]
    public async Task CountsEligibleRepositoriesAndAnnouncesEachReleaseOnce()
    {
        List<string> messages = [];
        NotificationService service = CreateService(new(), messages);
        Repo first = NewRepo("owner/first");
        Repo second = NewRepo("owner/second");
        Repo excluded = NewRepo("owner/excluded");
        excluded.ExcludedFromDownloadAll = true;
        Repo current = NewRepo("owner/current");
        current.CurrentInstallTag = current.Tag;
        Repo noRelease = NewRepo("owner/empty");
        noRelease.Tag = "";
        Repo[] repositories = [first, second, excluded, current, noRelease];
        await service.NotifyUpdatesAvailableAsync(repositories);
        await service.NotifyUpdatesAvailableAsync(repositories);
        Assert.Equal(new[] { "2 updates are available" }, messages);
        second.Tag = "v3";
        await service.NotifyUpdatesAvailableAsync(repositories);
        Assert.Equal(2, messages.Count);
        first.CurrentInstallTag = first.Tag;
        second.CurrentInstallTag = second.Tag;
        await service.NotifyUpdatesAvailableAsync(repositories);
        Assert.Equal(2, messages.Count);
        excluded.ExcludedFromDownloadAll = false;
        await service.NotifyUpdatesAvailableAsync(repositories);
        Assert.Equal("1 update is available", messages[2]);
    }

    [Fact]
    public async Task QuietHoursDeferAvailableUpdatesButSkipCompletions()
    {
        NotificationSettings settings = new() { LimitToTimeWindow = true };
        List<string> messages = [];
        DateTime now = new(2026, 10, 13, 7, 59, 0);
        NotificationService service = CreateService(settings, messages, () => now);
        Repo repo = NewRepo("owner/repo");
        await service.NotifyUpdatesAvailableAsync([repo]);
        await service.NotifyDownloadsCompletedAsync(1, repo.Name);
        Assert.Empty(messages);
        now = now.AddMinutes(1);
        await service.NotifyUpdatesAvailableAsync([repo]);
        Assert.Equal(new[] { "1 update is available" }, messages);
        await service.NotifyUpdatesAvailableAsync([repo]);
        Assert.Single(messages);
    }

    [Fact]
    public async Task DisabledTypesAreIndependentAndMasterSwitchMutesBoth()
    {
        NotificationSettings settings = new() { Enabled = false };
        List<string> messages = [];
        NotificationService service = CreateService(settings, messages);
        Repo repo = NewRepo("owner/repo");
        await service.NotifyUpdatesAvailableAsync([repo]);
        await service.NotifyDownloadsCompletedAsync(3);
        Assert.Empty(messages);
        settings.Enabled = true;
        settings.UpdatesAvailable = false;
        await service.NotifyUpdatesAvailableAsync([repo]);
        await service.NotifyDownloadsCompletedAsync(3);
        Assert.Equal(new[] { "Updates completed for 3 repositories" }, messages);
        settings.DownloadsCompleted = false;
        settings.UpdatesAvailable = true;
        await service.NotifyDownloadsCompletedAsync(1, repo.Name);
        await service.NotifyUpdatesAvailableAsync([repo]);
        Assert.Equal(2, messages.Count);
        Assert.Equal("1 update is available", messages[1]);
    }

    [Fact]
    public async Task CompletionIsOneMessagePerOperationAndEmptyBatchesAreSilent()
    {
        List<string> messages = [];
        NotificationService service = CreateService(new(), messages);
        await service.NotifyDownloadsCompletedAsync(0);
        Assert.Empty(messages);
        await service.NotifyDownloadsCompletedAsync(4);
        await service.NotifyDownloadsCompletedAsync(1, "owner/repo");
        Assert.Equal(new[] { "Updates completed for 4 repositories", "Update completed for owner/repo" }, messages);
    }

    [Fact]
    public async Task FailedDeliveryCanBeRetriedAndOverlappingChecksDoNotDuplicate()
    {
        TaskCompletionSource<bool> delivery = new();
        int attempts = 0;
        NotificationService service = new(() => new(), _ =>
        {
            attempts++;
            return delivery.Task;
        });
        Repo repo = NewRepo("owner/repo");
        Task first = service.NotifyUpdatesAvailableAsync([repo]);
        await service.NotifyUpdatesAvailableAsync([repo]);
        Assert.Equal(1, attempts);
        delivery.SetResult(false);
        await first;
        delivery = new();
        Task retry = service.NotifyUpdatesAvailableAsync([repo]);
        Assert.Equal(2, attempts);
        delivery.SetResult(true);
        await retry;
        await service.NotifyUpdatesAvailableAsync([repo]);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task UpdatesResolvedBeforeQuietHoursEndAreNotAnnounced()
    {
        NotificationSettings settings = new() { Enabled = false };
        List<string> messages = [];
        NotificationService service = CreateService(settings, messages);
        Repo repo = NewRepo("owner/repo");
        await service.NotifyUpdatesAvailableAsync([repo]);
        repo.CurrentInstallTag = repo.Tag;
        settings.Enabled = true;
        await service.NotifyUpdatesAvailableAsync([repo]);
        Assert.Empty(messages);
    }

    [Fact]
    public void NativeSenderPassesRepositoryTextAsData()
    {
        const string message = "owner/quotes\"; $(touch /tmp/never) <b>& --flag";
        System.Diagnostics.ProcessStartInfo start = DesktopNotificationSender.CreateStartInfo(message);
        Assert.False(start.UseShellExecute);
        Assert.True(start.RedirectStandardOutput);
        if (OperatingSystem.IsLinux())
        {
            Assert.Contains("--action=open=Open", start.ArgumentList);
            Assert.Contains("--action=default=Open", start.ArgumentList);
        }
        if (OperatingSystem.IsWindows()) Assert.Equal(message, start.Environment["GHD_NOTIFICATION_BODY"]);
        else Assert.Equal(message, start.ArgumentList.Last());
    }

    [Theory]
    [InlineData("open", true)]
    [InlineData("default", true)]
    [InlineData("dismissed", false)]
    [InlineData("", false)]
    public async Task NativeNotificationActionsOpenOnlyWhenRequested(string action, bool shouldOpen)
    {
        if (!OperatingSystem.IsLinux()) return;
        System.Diagnostics.ProcessStartInfo start = new("/bin/sh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("printf '%s\\n' \"$GHD_TEST_ACTION\"");
        start.Environment["GHD_TEST_ACTION"] = action;
        TaskCompletionSource<bool> activated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool delivered = await DesktopNotificationSender.SendAsync(start, () => activated.TrySetResult(true));
        Assert.True(delivered);
        if (shouldOpen) Assert.True(await activated.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        else Assert.False(activated.Task.IsCompleted);
    }

    private static Repo NewRepo(string name) => new()
    {
        Url = $"https://github.com/{name}", Name = name, Tag = "v2", CurrentInstallTag = "v1"
    };

    private static NotificationService CreateService(NotificationSettings settings, List<string> messages,
        Func<DateTime>? now = null) => new(() => settings, message =>
        {
            messages.Add(message);
            return Task.FromResult(true);
        }, now);
}
