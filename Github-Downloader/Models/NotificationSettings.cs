using System;

namespace Github_Downloader.Models;

public class NotificationSettings
{
    public bool Enabled { get; set; } = true;
    public bool UpdatesAvailable { get; set; } = true;
    public bool DownloadsCompleted { get; set; } = true;
    public bool LimitToTimeWindow { get; set; }
    public TimeSpan StartTime { get; set; } = TimeSpan.FromHours(8);
    public TimeSpan EndTime { get; set; } = TimeSpan.FromHours(22);

    public bool AllowsNotifications(DateTime localTime)
    {
        if (!Enabled) return false;
        if (!LimitToTimeWindow) return true;
        if (StartTime < TimeSpan.Zero || StartTime >= TimeSpan.FromDays(1)
            || EndTime < TimeSpan.Zero || EndTime >= TimeSpan.FromDays(1)) return false;

        TimeSpan time = localTime.TimeOfDay;
        if (StartTime == EndTime) return true;
        return StartTime < EndTime
            ? time >= StartTime && time < EndTime
            : time >= StartTime || time < EndTime;
    }
}
