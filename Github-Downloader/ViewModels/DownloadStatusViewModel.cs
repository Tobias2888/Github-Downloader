using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Github_Downloader.Models;
using LoggerLib;

namespace Github_Downloader.ViewModels;

public class DownloadStatusViewModel : ViewModelBase
{
    private const int MaxLogs = 1000;

    private DownloadStatus? _downloadStatus;
    private string _operationTitle = "Download Status";

    private string _statusText = "Checking for updates...";
    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    private string _progressText = "";

    public string ProgressText
    {
        get => _progressText;
        set
        {
            _progressText = value;
            OnPropertyChanged();
        }
    }

    private double? _percent;

    public double? Percent
    {
        get => _percent;
        set
        {
            double? clamped = value.HasValue ? Math.Clamp(value.Value, 0, 100) : null;
            if (_percent == clamped) return;
            _percent = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsIndeterminate));
        }
    }

    public bool IsIndeterminate => _percent is null;

    public ObservableCollection<DownloadLogEntry> Logs { get; } = [];

    private bool _isUpdating;

    public bool IsUpdating
    {
        get => _isUpdating;
        private set
        {
            if (_isUpdating == value) return;
            _isUpdating = value;
            if (_isUpdating)
            {
                Reset();
                Show();
            }
            OnPropertyChanged();
        }
    }

    public async Task<bool> RunAsync(Func<Task<bool>> operation, string title = "Download Status")
    {
        if (IsUpdating)
        {
            Logger.LogW("Operation skipped because another operation is running");
            return false;
        }

        Logger.LogD("Operation started");

        _operationTitle = title;
        IsUpdating = true;
        bool succeeded = false;
        try
        {
            succeeded = await operation();
        }
        catch (Exception exception)
        {
            Logger.LogE($"Operation failed ({exception.GetType().Name})");
            Log($"Operation failed ({exception.GetType().Name}). Check permissions, connectivity and installation requirements.", "error");
        }
        finally
        {
            IsUpdating = false;
        }

        if (succeeded)
        {
            Logger.LogD("Operation completed");
            Log("Completed", "success");
            Percent = 100;
            CloseDialog();
        }
        else
        {
            void ReportFailure()
            {
                if (Logs.Any(entry => entry.Level == "error")) return;
                Logger.LogE("Operation did not complete");
                Log("The operation could not complete. Check connectivity and installation requirements.", "error");
            }
            if (Dispatcher.UIThread.CheckAccess()) ReportFailure();
            else Dispatcher.UIThread.Post(ReportFailure);
        }
        return succeeded;
    }

    public void LogStatus(string message)
    {
        Log(message, message.StartsWith("Failed to ", StringComparison.Ordinal) ? "error" : "info");
    }

    public void LogProgress(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        const string prefix = "Downloaded: ";
        if (message.StartsWith(prefix) && message.EndsWith('%'))
        {
            // The library formats this with the current culture (e.g. "45,50" on de-AT), so
            // parse with the current culture first and fall back to invariant for "45.50".
            string number = message[prefix.Length..^1].Trim();
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.CurrentCulture, out double percent)
                || double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
            {
                Percent = percent;
                return;
            }
        }

        Log(message, "detail");
    }

    public void Log(string message, string level = "info")
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        void Add()
        {
            StatusText = message;
            Logs.Add(new DownloadLogEntry
            {
                Timestamp = DateTime.Now,
                Message = message,
                Level = level
            });

            while (Logs.Count > MaxLogs)
            {
                Logs.RemoveAt(0);
            }
        }

        if (Dispatcher.UIThread.CheckAccess()) Add();
        else Dispatcher.UIThread.Post(Add);
    }

    private void Reset()
    {
        void ResetOnUi()
        {
            Logs.Clear();
            StatusText = "Starting...";
            ProgressText = "";
            Percent = null;
        }

        if (Dispatcher.UIThread.CheckAccess()) ResetOnUi();
        else Dispatcher.UIThread.Post(ResetOnUi);
    }

    public bool Show()
    {
        if (_downloadStatus is { IsVisible: true }) return true;

        Window mainWindow = ((App)Application.Current!).MainWindow;
        if (mainWindow is null) return false;
        if (!mainWindow.IsVisible) return false;

        _downloadStatus = new()
        {
            DataContext = this,
            Title = _operationTitle
        };
        _downloadStatus.Closed += (_, _) =>
        {
            Logger.LogD("Operation status window closed");
            _downloadStatus = null;
        };
        _downloadStatus.Show(mainWindow);
        Logger.LogD("Operation status window opened");
        return true;
    }

    public void CloseDialog()
    {
        _downloadStatus?.Close();
    }
}
