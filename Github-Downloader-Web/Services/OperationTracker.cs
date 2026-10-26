using Github_Downloader_Web.DTOs;
using LoggerLib;
using DownloadProgress = Github_Downloader_lib.DownloadProgress;

namespace Github_Downloader_Web.Services;

public sealed class OperationTracker
{
    private const int MaxLogs = 1000;

    private readonly object _lock = new();
    private readonly List<OperationLogEntry> _logs = [];

    private bool _isRunning;
    private string _title = "";
    private string _status = "";
    private double? _itemPercent;
    private DownloadProgress? _transfer;
    private bool _itemStarted;
    private bool _installing;
    private int _completedItems;
    private int _totalItems;
    private DateTime? _startedAt;
    private DateTime? _finishedAt;
    private bool? _success;
    private string? _error;

    public void Start(string title, int totalItems)
    {
        Logger.LogI($"Web operation started (items {totalItems})");
        lock (_lock)
        {
            _logs.Clear();
            _isRunning = true;
            _title = title;
            _status = "Starting...";
            _itemPercent = null;
            _transfer = null;
            _itemStarted = false;
            _installing = false;
            _completedItems = 0;
            _totalItems = totalItems;
            _startedAt = DateTime.UtcNow;
            _finishedAt = null;
            _success = null;
            _error = null;
            AddLogUnsafe(title);
        }
    }

    public void Log(string message, string level = "info")
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        lock (_lock)
        {
            _status = message;
            if (_transfer is not null) _itemPercent = null;
            _transfer = null;
            if (message == "Installing Updates...") _installing = true;
            AddLogUnsafe(message, level);
        }
    }

    public void StartItem()
    {
        lock (_lock)
        {
            if (_itemStarted) _completedItems++;
            _itemStarted = true;
            _itemPercent = null;
            _transfer = null;
        }
    }

    public void SetTransferProgress(DownloadProgress progress)
    {
        lock (_lock)
        {
            _transfer = progress;
            _itemPercent = progress.Percent;
        }
    }

    public void SetItemPercent(double percent, string? status = null)
    {
        lock (_lock)
        {
            _itemPercent = Math.Clamp(percent, 0, 100);
            if (status is not null)
            {
                _status = status;
                _transfer = null;
            }
        }
    }

    public void Finish(bool success, string? error = null)
    {
        if (success) Logger.LogI("Web operation completed");
        else Logger.LogE("Web operation failed");
        lock (_lock)
        {
            _isRunning = false;
            _transfer = null;
            _success = success;
            _error = error;
            _finishedAt = DateTime.UtcNow;

            if (success)
            {
                _completedItems = _totalItems;
                AddLogUnsafe("Completed", "success");
            }
            else
            {
                AddLogUnsafe(error ?? "Operation failed", "error");
            }
        }
    }

    private void AddLogUnsafe(string message, string level = "info")
    {
        _logs.Add(new OperationLogEntry(DateTime.UtcNow, message, level));
        if (_logs.Count > MaxLogs)
        {
            _logs.RemoveRange(0, _logs.Count - MaxLogs);
        }
    }

    public OperationProgressResponse Snapshot()
    {
        lock (_lock)
        {
            double? percent = null;
            if (_totalItems > 0)
            {
                percent = ((_completedItems + (_itemPercent ?? 0) / 100.0) / _totalItems) * 100.0;
            }
            else if (_itemPercent.HasValue)
            {
                percent = _itemPercent;
            }

            if (!_isRunning && _success == true) percent = 100;

            // Keep a sliver of the bar free while the install phase runs on top of finished downloads.
            if (_isRunning && _installing && percent.HasValue)
            {
                percent = Math.Min(percent.Value, 99);
            }

            if (percent.HasValue) percent = Math.Clamp(percent.Value, 0, 100);

            bool indeterminate = _isRunning && _itemPercent == null
                && (_transfer is not null || _completedItems == 0 && !_installing);
            if (indeterminate) percent = null;

            return new OperationProgressResponse(
                _isRunning,
                _title,
                _status,
                percent,
                indeterminate,
                _completedItems,
                _totalItems,
                _startedAt,
                _finishedAt,
                _success,
                _error,
                _logs.ToList(),
                _transfer
            );
        }
    }
}
