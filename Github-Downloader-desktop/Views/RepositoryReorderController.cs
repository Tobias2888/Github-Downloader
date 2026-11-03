using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Github_Downloader_lib.Models;
using LoggerLib;

namespace Github_Downloader.Views;

internal sealed class RepositoryReorderController
{
    private const double AnimationSeconds = 0.16;
    private readonly HomeView _host;
    private readonly ItemsControl _items;
    private readonly ScrollViewer _scroll;
    private readonly Canvas _overlay;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _animation = new();
    private readonly Stopwatch _ticks = new();
    private List<Row> _rows = [];
    private ObservableCollection<Repo>? _repos;
    private Row? _dragged;
    private RepoEntryControl? _preview;
    private IPointer? _pointer;
    private TopLevel? _root;
    private Point _grabOffset;
    private Point _cursor;
    private int _targetIndex;
    private bool _started;
    private Cursor? _originalCursor;

    public RepositoryReorderController(HomeView host, ItemsControl items, ScrollViewer scroll, Canvas overlay)
    {
        _host = host;
        _items = items;
        _scroll = scroll;
        _overlay = overlay;
        host.PointerMoved += OnPointerMoved;
        host.PointerReleased += OnPointerReleased;
        host.PointerCaptureLost += OnCaptureLost;
        host.DetachedFromVisualTree += OnDetached;
        _timer.Tick += OnTick;
    }

    public void Begin(RepoEntryControl control, PointerPressedEventArgs e)
    {
        if (_pointer is not null || control.DataContext is not Repo repo
            || _items.ItemsSource is not ObservableCollection<Repo> repos || repos.Count < 2) return;

        ResetRows();
        _rows = GetRows();
        _dragged = _rows.Find(row => ReferenceEquals(row.Repo, repo));
        if (_dragged is null) return;
        _repos = repos;
        _targetIndex = repos.IndexOf(repo);
        _grabOffset = e.GetPosition(control);
        _cursor = e.GetPosition(_overlay);
        _pointer = e.Pointer;
        _originalCursor = _host.Cursor;
        _root = TopLevel.GetTopLevel(_host);
        if (_root is not null) _root.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        repos.CollectionChanged += OnCollectionChanged;
        _pointer.Capture(_host);
        e.Handled = true;
    }

    private List<Row> GetRows() => _items.GetVisualDescendants().OfType<RepoEntryControl>()
        .Where(control => control.DataContext is Repo)
        .Select(control => new Row(control, control.TranslatePoint(default, _items)!.Value.Y))
        .OrderBy(row => row.Top).ToList();

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pointer != e.Pointer || _dragged is null) return;
        _cursor = e.GetPosition(_overlay);
        Point point = e.GetPosition(_dragged.Control);
        Vector distance = new(point.X - _grabOffset.X, point.Y - _grabOffset.Y);
        if (!_started && distance.Length < 4) return;
        if (!_started) StartPreview();
        PositionPreview();
        UpdateTarget();
        e.Handled = true;
    }

    private void StartPreview()
    {
        _started = true;
        Logger.LogD("Repository drag started");
        _preview = new RepoEntryControl
        {
            DataContext = _dragged!.Repo,
            Width = _dragged.Control.Bounds.Width,
            Height = _dragged.Control.Bounds.Height,
            Opacity = 0.96,
            IsHitTestVisible = false,
            RenderTransform = new TranslateTransform()
        };
        Border card = _preview.FindControl<Border>("RepoCard")!;
        card.BorderBrush = Brush.Parse("#18AFB5");
        card.BoxShadow = BoxShadows.Parse("0 8 24 0 #88000000");
        _overlay.Children.Add(_preview);
        _dragged.Control.Opacity = 0.2;
        _dragged.Control.IsHitTestVisible = false;
        _host.Cursor = new Cursor(StandardCursorType.Hand);
        _ticks.Restart();
        _timer.Start();
    }

    private void PositionPreview()
    {
        TranslateTransform translation = (TranslateTransform)_preview!.RenderTransform!;
        translation.X = _cursor.X - _grabOffset.X;
        translation.Y = _cursor.Y - _grabOffset.Y;
    }

    private void UpdateTarget()
    {
        Point? point = _overlay.TranslatePoint(_cursor, _items);
        if (point is null || _dragged is null) return;
        double center = point.Value.Y - _grabOffset.Y + _dragged.Height / 2;
        int index = _rows.Count(row => row != _dragged && center > row.Top + row.Height / 2);
        if (index == _targetIndex) return;
        _targetIndex = index;

        List<Row> order = _rows.Where(row => row != _dragged).ToList();
        order.Insert(index, _dragged);
        double top = _rows[0].Top;
        foreach (Row row in order)
        {
            row.AnimateTo(new Vector(0, top - row.Top));
            top += row.Height;
        }
        _animation.Restart();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double elapsed = Math.Min(_ticks.Elapsed.TotalSeconds, 0.05);
        _ticks.Restart();
        if (_started && _dragged is not null)
        {
            Point? cursor = _overlay.TranslatePoint(_cursor, _scroll);
            if (cursor is not null)
            {
                const double edge = 48;
                double y = cursor.Value.Y;
                double velocity = y < edge ? -Math.Clamp((edge - y) / edge, 0, 1)
                    : y > _scroll.Bounds.Height - edge ? Math.Clamp((y - _scroll.Bounds.Height + edge) / edge, 0, 1) : 0;
                double offset = Math.Clamp(_scroll.Offset.Y + velocity * 850 * elapsed,
                    0, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height));
                if (Math.Abs(offset - _scroll.Offset.Y) > 0.01)
                {
                    _scroll.Offset = new Vector(_scroll.Offset.X, offset);
                    _scroll.UpdateLayout();
                    UpdateTarget();
                }
            }
        }

        double progress = _animation.IsRunning ? Math.Min(1, _animation.Elapsed.TotalSeconds / AnimationSeconds) : 1;
        double eased = 1 - Math.Pow(1 - progress, 3);
        foreach (Row row in _rows) row.Update(eased);
        if (!_started && progress >= 1) ResetRows();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pointer != e.Pointer || e.InitialPressMouseButton != MouseButton.Left) return;
        Finish(commit: true);
        e.Handled = true;
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(e.Source, _host) && _pointer is not null) Finish(commit: false);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _pointer is null) return;
        Finish(commit: false);
        e.Handled = true;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Finish(commit: false);

    private void Finish(bool commit)
    {
        Row? dragged = _dragged;
        ObservableCollection<Repo>? repos = _repos;
        bool animate = _started;
        Dictionary<Repo, Point> positions = _rows.ToDictionary(row => row.Repo,
            row => row.Control.TranslatePoint(default, _items) ?? new Point(0, row.Top));
        if (_preview is not null && dragged is not null)
            positions[dragged.Repo] = _preview.TranslatePoint(default, _items) ?? positions[dragged.Repo];

        ReleasePointer();
        ResetRows();
        if (_preview is not null) _overlay.Children.Remove(_preview);
        _preview = null;
        _dragged = null;
        _started = false;
        _host.Cursor = _originalCursor;

        if (commit && animate && dragged is not null && repos is not null)
        {
            int oldIndex = repos.IndexOf(dragged.Repo);
            if (oldIndex >= 0 && oldIndex != _targetIndex)
            {
                Logger.LogD("Applying repository order");
                repos.Move(oldIndex, _targetIndex);
                Logger.LogI("Repository order applied");
            }
            else Logger.LogD("Repository drag finished without an order change");
        }
        if (animate && !commit) Logger.LogI("Repository drag cancelled");
        if (!animate) return;

        _items.UpdateLayout();
        _rows = GetRows();
        foreach (Row row in _rows)
        {
            if (positions.TryGetValue(row.Repo, out Point previous))
            {
                Point current = row.Control.TranslatePoint(default, _items)!.Value;
                row.SetOffset(new Vector(previous.X - current.X, previous.Y - current.Y));
                row.AnimateTo(default);
                if (dragged is not null && ReferenceEquals(row.Repo, dragged.Repo)) row.Raise();
            }
        }
        _animation.Restart();
        _ticks.Restart();
        _timer.Start();
    }

    private void ReleasePointer()
    {
        IPointer? pointer = _pointer;
        _pointer = null;
        if (_repos is not null) _repos.CollectionChanged -= OnCollectionChanged;
        _repos = null;
        if (_root is not null) _root.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _root = null;
        pointer?.Capture(null);
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_started) Logger.LogI("Repository drag cancelled because the view closed");
        ReleasePointer();
        if (_preview is not null) _overlay.Children.Remove(_preview);
        _preview = null;
        _dragged = null;
        _started = false;
        _host.Cursor = _originalCursor;
        ResetRows();
    }

    private void ResetRows()
    {
        _timer.Stop();
        _animation.Reset();
        foreach (Row row in _rows) row.Reset();
        _rows.Clear();
    }

    private sealed class Row
    {
        private readonly ITransform? _originalTransform;
        private readonly double _opacity;
        private readonly bool _hitTestVisible;
        private readonly Control? _container;
        private readonly int _zIndex;
        private readonly TranslateTransform _transform = new();
        private Vector _from;
        private Vector _to;

        public RepoEntryControl Control { get; }
        public Repo Repo { get; }
        public double Top { get; }
        public double Height => Control.Bounds.Height;

        public Row(RepoEntryControl control, double top)
        {
            Control = control;
            Repo = (Repo)control.DataContext!;
            Top = top;
            _originalTransform = control.RenderTransform;
            _opacity = control.Opacity;
            _hitTestVisible = control.IsHitTestVisible;
            _container = control.GetVisualParent() as Control;
            _zIndex = _container?.ZIndex ?? 0;
            control.RenderTransform = _transform;
        }

        public void SetOffset(Vector offset)
        {
            _transform.X = offset.X;
            _transform.Y = offset.Y;
        }

        public void AnimateTo(Vector offset)
        {
            _from = new Vector(_transform.X, _transform.Y);
            _to = offset;
        }

        public void Update(double progress) => SetOffset(_from + (_to - _from) * progress);

        public void Raise()
        {
            if (_container is not null) _container.ZIndex = 100;
        }

        public void Reset()
        {
            Control.RenderTransform = _originalTransform;
            Control.Opacity = _opacity;
            Control.IsHitTestVisible = _hitTestVisible;
            if (_container is not null) _container.ZIndex = _zIndex;
        }
    }
}
