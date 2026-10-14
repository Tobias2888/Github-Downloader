using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Github_Downloader.Views;

internal sealed class TimePickerScrollAnimation
{
    private const double DurationMilliseconds = 140;
    private readonly DateTimePickerPanel _panel;
    private readonly TranslateTransform _translation = new();
    private readonly Stopwatch _elapsed = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private int _previousValue;
    private double _startOffset;

    public TimePickerScrollAnimation(DateTimePickerPanel panel)
    {
        _panel = panel;
        _previousValue = panel.SelectedValue;
        panel.RenderTransform = _translation;
        panel.SelectionChanged += OnSelectionChanged;
        panel.DetachedFromVisualTree += OnDetached;
        _timer.Tick += OnAnimationTick;
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        int difference = _panel.SelectedValue - _previousValue;
        _previousValue = _panel.SelectedValue;

        // Preserve the direction when the wheel wraps between 23/00 or 59/00.
        int range = _panel.MaximumValue - _panel.MinimumValue + _panel.Increment;
        if (_panel.ShouldLoop && Math.Abs(difference) > range / 2)
            difference -= Math.Sign(difference) * range;

        if (Math.Abs(difference) != _panel.Increment || !_panel.IsAttachedToVisualTree()
            || !_panel.IsEffectivelyVisible || _panel.Bounds.Height == 0)
        {
            Reset();
            return;
        }

        // The native panel updates its rows immediately. Offset the new rows to
        // their previous positions, then ease them into the selected position.
        _startOffset = Math.Clamp(_translation.Y + Math.Sign(difference) * _panel.ItemHeight,
            -2 * _panel.ItemHeight, 2 * _panel.ItemHeight);
        _translation.Y = _startOffset;
        _elapsed.Restart();
        _timer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        double progress = Math.Min(1, _elapsed.Elapsed.TotalMilliseconds / DurationMilliseconds);
        _translation.Y = _startOffset * Math.Pow(1 - progress, 3);
        if (progress >= 1) Reset();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Reset();
    }

    private void Reset()
    {
        _timer.Stop();
        _elapsed.Reset();
        _translation.Y = 0;
    }
}
