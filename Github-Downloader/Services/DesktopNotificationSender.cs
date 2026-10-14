using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LoggerLib;

namespace Github_Downloader.Services;

public static class DesktopNotificationSender
{
    public static Task<bool> SendAsync(string message) => SendAsync(message, null);

    public static async Task<bool> SendAsync(string message, Action? openWindow)
    {
        try
        {
            return await SendAsync(CreateStartInfo(message), openWindow);
        }
        catch (Exception exception)
        {
            // Notification failures must never turn a completed download into a failed operation.
            Logger.LogE($"Unable to show desktop notification: {exception.Message}");
        }
        return false;
    }

    internal static async Task<bool> SendAsync(ProcessStartInfo start, Action? openWindow)
    {
        Process? process = Process.Start(start);
        if (process is null) return false;

        Task<bool> delivery = ObserveAsync(process, openWindow);
        // Action-capable senders stay alive while the notification is visible. Do not hold up
        // saving a completed download or subsequent update checks while waiting for a click.
        Task finished = await Task.WhenAny(delivery, Task.Delay(200));
        return finished == delivery ? await delivery : true;
    }

    private static async Task<bool> ObserveAsync(Process process, Action? openWindow)
    {
        using (process)
        using (CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2)))
        {
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                string response = await output;
                string error = await errors;
                if (process.ExitCode != 0)
                {
                    Logger.LogE($"Desktop notification failed (exit code {process.ExitCode}): {error.Trim()}");
                    return false;
                }
                foreach (string action in response.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (action is not ("open" or "default")) continue;
                    openWindow?.Invoke();
                    break;
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(true);
                return false;
            }
            catch (Exception exception)
            {
                Logger.LogE($"Unable to handle desktop notification: {exception.Message}");
                return false;
            }
        }
    }

    public static ProcessStartInfo CreateStartInfo(string message)
    {
        ProcessStartInfo start = new()
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (OperatingSystem.IsLinux())
        {
            start.FileName = "notify-send";
            start.ArgumentList.Add("--app-name=GitHub Downloader");
            start.ArgumentList.Add("--icon=software-update-available");
            start.ArgumentList.Add("--expire-time=8000");
            start.ArgumentList.Add("--action=open=Open");
            start.ArgumentList.Add("--");
            start.ArgumentList.Add("GitHub Downloader");
            start.ArgumentList.Add(message);
        }
        else if (OperatingSystem.IsWindows())
        {
            start.FileName = "powershell.exe";
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-STA");
            start.ArgumentList.Add("-EncodedCommand");
            const string script = "Add-Type -AssemblyName System.Windows.Forms; "
                + "Add-Type -AssemblyName System.Drawing; "
                + "$icon = New-Object System.Windows.Forms.NotifyIcon; "
                + "try { $icon.Icon = [System.Drawing.SystemIcons]::Information; $icon.Visible = $true; "
                + "Register-ObjectEvent -InputObject $icon -EventName BalloonTipClicked -SourceIdentifier ghd-open | Out-Null; "
                + "$icon.ShowBalloonTip(5000, 'GitHub Downloader', $env:GHD_NOTIFICATION_BODY, "
                + "[System.Windows.Forms.ToolTipIcon]::Info); $until = [DateTime]::UtcNow.AddSeconds(15); "
                + "while ([DateTime]::UtcNow -lt $until) { [System.Windows.Forms.Application]::DoEvents(); "
                + "if (Get-Event -SourceIdentifier ghd-open -ErrorAction SilentlyContinue) { Write-Output 'open'; break }; "
                + "Start-Sleep -Milliseconds 100 } } "
                + "finally { Unregister-Event -SourceIdentifier ghd-open -ErrorAction SilentlyContinue; $icon.Dispose() }";
            start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            start.Environment["GHD_NOTIFICATION_BODY"] = message;
        }
        else if (OperatingSystem.IsMacOS())
        {
            start.FileName = "/usr/bin/osascript";
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("on run argv\ndisplay notification (item 1 of argv) with title \"GitHub Downloader\"\nend run");
            start.ArgumentList.Add(message);
        }
        else throw new PlatformNotSupportedException("Desktop notifications are unavailable on this platform.");
        return start;
    }
}
