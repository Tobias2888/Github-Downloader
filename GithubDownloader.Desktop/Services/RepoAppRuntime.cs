using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GithubDownloader.Core.Models;

namespace GithubDownloader.Desktop.Services;

internal static class RepoAppRuntime
{
    private static readonly Dictionary<string, RepoAppLogSession> Sessions = new(StringComparer.OrdinalIgnoreCase);

    internal static bool CanOpen(Repo repo) => RepoDesktopEntry.Resolve(repo) is not null;

    internal static RepoAppLogSession? GetSession(Repo repo) => Sessions.GetValueOrDefault(repo.Url);

    internal static bool TryOpen(Repo repo, out RepoAppLogSession? session, out string error)
    {
        session = null;
        error = "";
        if (!OperatingSystem.IsLinux())
        {
            error = "Opening installed applications with captured logs is available on Linux.";
            return false;
        }
        RepoDesktopEntry? entry = RepoDesktopEntry.Resolve(repo);
        if (entry is null)
        {
            error = "Could not find one supported application-menu entry for this repository.";
            return false;
        }

        ProcessStartInfo start = new(entry.Executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (entry.WorkingDirectory is not null) start.WorkingDirectory = entry.WorkingDirectory;
        foreach (string argument in entry.Arguments) start.ArgumentList.Add(argument);

        try
        {
            Process process = new() { StartInfo = start };
            if (!process.Start())
            {
                process.Dispose();
                error = "The application could not be started.";
                return false;
            }
            session = new RepoAppLogSession(repo.Name);
            Sessions[repo.Url] = session;
            _ = session.CaptureAsync(process);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception
                                           or IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = $"Could not start {repo.Name}: {exception.Message}";
            return false;
        }
    }
}
