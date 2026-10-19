using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Github_Downloader.Views;
using Github_Downloader.ViewModels;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

[Collection("Repository dragging")]
public sealed class HistoryWindowTests
{
    private readonly RepositoryDragApplication _application;
    public HistoryWindowTests(RepositoryDragApplication application) => _application = application;

    [Fact]
    public async Task HistoryViewFiltersResultsAndClearsOnlyAfterConfirmation()
    {
        string root = Path.Combine(Path.GetTempPath(), "ghd-history-ui-" + Guid.NewGuid().ToString("N"));
        await _application.Dispatch(async () =>
        {
            HistoryStore store = new(Path.Combine(root, "history.json"));
            Repo repo = new() { Name = "owner/repository", Url = "https://github.com/owner/repository", Tag = "v2", CurrentInstallTag = "v1" };
            using (HistoryScope success = store.Begin(HistoryAction.Install, repo)) success.Complete(HistoryOutcome.Installed);
            using (HistoryScope failure = store.Begin(HistoryAction.Reinstall, repo)) failure.Finish(HistoryStatus.Failed, HistoryOutcome.InstallFailed);
            using (HistoryScope skipped = store.Begin(HistoryAction.Update, repo)) skipped.Finish(HistoryStatus.Skipped, HistoryOutcome.AlreadyCurrent);
            using HistoryScope running = store.Begin(HistoryAction.Download, repo);
            HistoryWindow window = new(store);
            window.Show();
            try
            {
                window.UpdateLayout();
                await Task.Delay(30);
                HistoryViewModel model = Assert.IsType<HistoryViewModel>(window.DataContext);
                Assert.Equal(3, model.Entries.Count);
                window.FindControl<CalendarDatePicker>("FromDate")!.SelectedDate = DateTime.Today.AddDays(1);
                Assert.Empty(model.Entries);
                window.FindControl<CalendarDatePicker>("FromDate")!.SelectedDate = null;
                Assert.Equal(3, model.Entries.Count);
                window.FindControl<CalendarDatePicker>("ToDate")!.SelectedDate = DateTime.Today.AddDays(-1);
                Assert.Empty(model.Entries);
                window.FindControl<CalendarDatePicker>("ToDate")!.SelectedDate = null;
                Assert.Equal(3, model.Entries.Count);
                window.FindControl<CheckBox>("IncludeSkipped")!.IsChecked = true;
                Assert.Equal(4, model.Entries.Count);
                window.FindControl<CheckBox>("IncludeSkipped")!.IsChecked = false;
                window.FindControl<ComboBox>("ResultFilter")!.SelectedItem = "Skipped";
                Assert.Equal("Skipped", Assert.Single(model.Entries).Result);
                window.FindControl<ComboBox>("ResultFilter")!.SelectedItem = "Failed";
                Assert.Equal("Failed", Assert.Single(model.Entries).Result);
                window.FindControl<TextBox>("RepositoryFilter")!.Text = "missing";
                Assert.Empty(model.Entries);
                Assert.Equal(3, store.Query().Total);
                Assert.False(window.FindControl<Border>("ClearConfirmation")!.IsVisible);
                window.FindControl<TextBox>("RepositoryFilter")!.Text = "";
                window.FindControl<ComboBox>("ResultFilter")!.SelectedIndex = 0;
                window.UpdateLayout();
                window.CaptureRenderedFrame()?.Save("/tmp/github-downloader-history-desktop.png");
                window.FindControl<Button>("ClearHistory")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(window.FindControl<Border>("ClearConfirmation")!.IsVisible);
                Assert.Equal(3, store.Query().Total);
                window.FindControl<Button>("ConfirmClear")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(running.Id, Assert.Single(store.Query().Entries).Id);
                Assert.Single(model.Entries);
            }
            finally
            {
                window.Close();
                running.Complete(HistoryOutcome.Downloaded);
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }, CancellationToken.None);
    }
}
