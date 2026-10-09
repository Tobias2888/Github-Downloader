using Github_Downloader_Web.DTOs;

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
        lock (_lock)
        {
            _logs.Clear();
            _isRunning = true;
            _title = title;
            _status = "Starting...";
            _itemPercent = null;
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
        }
    }

    public void SetItemPercent(double percent)
    {
        lock (_lock) _itemPercent = Math.Clamp(percent, 0, 100);
    }

    public void Finish(bool success, string? error = null)
    {
        lock (_lock)
        {
            _isRunning = false;
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

            var indeterminate = _isRunning && _itemPercent == null && _completedItems == 0 && !_installing;

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
                _logs.ToList()
            );
        }
    }
}
