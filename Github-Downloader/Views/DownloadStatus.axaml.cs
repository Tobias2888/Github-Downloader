using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Github_Downloader;

public partial class DownloadStatus : Window
{
    private ViewModels.DownloadStatusViewModel? _viewModel;

    public DownloadStatus()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SubscribeToViewModel(DataContext as ViewModels.DownloadStatusViewModel);
    }

    private void SubscribeToViewModel(ViewModels.DownloadStatusViewModel? viewModel)
    {
        if (_viewModel == viewModel) return;

        if (_viewModel != null)
        {
            _viewModel.Logs.CollectionChanged -= OnLogsCollectionChanged;
        }

        _viewModel = viewModel;

        if (_viewModel != null)
        {
            _viewModel.Logs.CollectionChanged += OnLogsCollectionChanged;
        }
    }

    private void OnLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        LogScroll?.ScrollToEnd();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        SubscribeToViewModel(null);
    }

    private void BtnClose_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}