using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace GithubDownloader.Desktop.Services;

public sealed class RepoAppLogSession
{
    private const int MaximumLines = 1000;
    private const int MaximumLineLength = 16384;
    internal RepoAppLogSession(string title)
    {
        Title = title;
    }

    public string Title { get; }
    public ObservableCollection<string> Lines { get; } = [];
    public bool HasOutput => Lines.Count > 0;

    internal void Add(string stream, string line)
    {
        string boundedLine = line.Length > MaximumLineLength ? line[..MaximumLineLength] + " …[line truncated]" : line;
        void AddOnUiThread()
        {
            Lines.Add($"[{stream}] {boundedLine}");
            while (Lines.Count > MaximumLines) Lines.RemoveAt(0);
        }

        if (Dispatcher.UIThread.CheckAccess()) AddOnUiThread();
        else Dispatcher.UIThread.Post(AddOnUiThread);
    }

    internal async Task CaptureAsync(Process process)
    {
        Task output = ReadLinesAsync(process.StandardOutput, "out");
        Task error = ReadLinesAsync(process.StandardError, "err");
        await Task.WhenAll(output, error);
        await process.WaitForExitAsync();
        Add("ghd", $"Process exited with code {process.ExitCode}.");
        process.Dispose();
    }

    private async Task ReadLinesAsync(StreamReader reader, string stream)
    {
        char[] buffer = new char[2048];
        StringBuilder line = new();
        bool truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None)) > 0)
        {
            for (int index = 0; index < count; index++)
            {
                char character = buffer[index];
                if (character == '\n')
                {
                    Add(stream, line.ToString() + (truncated ? " …[line truncated]" : ""));
                    line.Clear();
                    truncated = false;
                }
                else if (character != '\r')
                {
                    if (line.Length < MaximumLineLength) line.Append(character);
                    else truncated = true;
                }
            }
        }
        if (line.Length > 0 || truncated) Add(stream, line.ToString() + (truncated ? " …[line truncated]" : ""));
    }
}
