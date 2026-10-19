using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Github_Downloader.ViewModels;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;

namespace Github_Downloader.Views;

public partial class HistoryWindow : Window
{
    private readonly HistoryStore _store;
    private readonly HistoryViewModel _model = new();
    private int _offset;
    private const int PageSize = 100;

    public HistoryWindow() : this(OperationHistory.Store) { }

    internal HistoryWindow(HistoryStore store)
    {
        InitializeComponent();
        _store = store;
        DataContext = _model;
        ResultFilter.ItemsSource = new[] { "All results", "Succeeded", "Failed", "PartialFailure", "Skipped", "Interrupted", "Running" };
        ResultFilter.SelectedIndex = 0;
        RepositoryFilter.PropertyChanged += Filter_OnPropertyChanged;
        FromDate.PropertyChanged += Filter_OnPropertyChanged;
        ToDate.PropertyChanged += Filter_OnPropertyChanged;
        ResultFilter.PropertyChanged += Filter_OnPropertyChanged;
        IncludeSteps.PropertyChanged += Filter_OnPropertyChanged;
        IncludeSkipped.PropertyChanged += Filter_OnPropertyChanged;
        Refresh();
    }

    internal void Refresh()
    {
        try
        {
            HistoryStatus? status = ResultFilter.SelectedIndex > 0
                ? Enum.Parse<HistoryStatus>((string)ResultFilter.SelectedItem!) : null;
            DateTimeOffset? from = FromDate.SelectedDate?.Date;
            DateTimeOffset? to = ToDate.SelectedDate?.Date.AddDays(1);
            HistoryPage page = _store.Query(new(RepositoryFilter.Text, from, to, status,
                Offset: _offset, Limit: PageSize, IncludeSteps: IncludeSteps.IsChecked == true, IncludeSkipped: IncludeSkipped.IsChecked == true));
            _model.Entries.Clear();
            foreach (HistoryEntry entry in page.Entries) _model.Entries.Add(new(entry));
            PageStatus.Text = page.Total == 0 ? "No history matches these filters."
                : $"{page.Offset + 1}–{page.Offset + page.Entries.Count} of {page.Total} entries";
            PreviousPage.IsEnabled = _offset > 0;
            NextPage.IsEnabled = _offset + page.Entries.Count < page.Total;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            PageStatus.Text = exception is ArgumentException ? "Choose a valid date range."
                : "History could not be read. Check file access; existing history was preserved.";
        }
    }

    private void Filter_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.TextProperty && e.Property != CalendarDatePicker.SelectedDateProperty
            && e.Property != SelectingItemsControl.SelectedItemProperty && e.Property != ToggleButton.IsCheckedProperty)
            return;
        _offset = 0;
        Refresh();
    }
    private void Previous_OnClick(object? sender, RoutedEventArgs e) { _offset = Math.Max(0, _offset - PageSize); Refresh(); }
    private void Next_OnClick(object? sender, RoutedEventArgs e) { _offset += PageSize; Refresh(); }
    private void Reset_OnClick(object? sender, RoutedEventArgs e)
    {
        RepositoryFilter.Text = "";
        FromDate.SelectedDate = null;
        ToDate.SelectedDate = null;
        ResultFilter.SelectedIndex = 0;
        IncludeSteps.IsChecked = false;
        IncludeSkipped.IsChecked = false;
        _offset = 0;
        Refresh();
    }
    private void Clear_OnClick(object? sender, RoutedEventArgs e) => ClearConfirmation.IsVisible = true;
    private void CancelClear_OnClick(object? sender, RoutedEventArgs e) => ClearConfirmation.IsVisible = false;
    private void ConfirmClear_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            _store.Clear();
            ClearConfirmation.IsVisible = false;
            _offset = 0;
            Refresh();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PageStatus.Text = "History could not be cleared. Check file access.";
        }
    }
}
