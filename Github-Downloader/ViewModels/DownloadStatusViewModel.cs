using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Github_Downloader.Models;

namespace Github_Downloader.ViewModels;

public class DownloadStatusViewModel : ViewModelBase
{
    private const int MaxLogs = 1000;

    //public Window? MainWindow { get; set; }
    private DownloadStatus? _downloadStatus;

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
        set
        {
            if (_isUpdating == value) return;
            _isUpdating = value;
            if (_isUpdating)
            {
                Reset();
                Show();
            }
            else
            {
                Log("Completed", "success");
                Percent = 100;
            }
            OnPropertyChanged();
        }
    }

    public void LogStatus(string message)
    {
        Log(message);
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
            DataContext = this
        };
        _downloadStatus.Closed += (_, _) => _downloadStatus = null;
        _downloadStatus.Show(mainWindow);
        return true;
    }

    public void CloseDialog()
    {
        _downloadStatus?.Close();
    }
}
