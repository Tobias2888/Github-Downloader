using Avalonia.Controls;
using Avalonia.Interactivity;
using Github_Downloader.Views;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

[Collection("Repository dragging")]
public sealed class SettingsViewTests
{
    private readonly RepositoryDragApplication _application;
    public SettingsViewTests(RepositoryDragApplication application) => _application = application;

    [Fact]
    public async Task ReinstallAllRequiresConfirmationAndIncludesExcludedRepositories()
    {
        await _application.Dispatch(() =>
        {
            Repo[] previous = UpdateManager.Repos.ToArray();
            UpdateManager.Repos.Clear();
            Repo excluded = new() { Url = "https://github.com/owner/excluded", Name = "owner/excluded", ExcludedFromDownloadAll = true };
            UpdateManager.Repos.Add(new Repo { Url = "https://github.com/owner/included", Name = "owner/included" });
            UpdateManager.Repos.Add(excluded);
            try
            {
                SettingsView view = new();
                Border confirmation = view.FindControl<Border>("ReinstallConfirmation")!;
                Button reinstall = view.FindControl<Button>("ReinstallAll")!;
                Button confirm = view.FindControl<Button>("ConfirmReinstall")!;
                Assert.False(confirmation.IsVisible);

                reinstall.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(confirmation.IsVisible);
                Assert.Equal("Re-download and reinstall all 2 repositories?",
                    view.FindControl<TextBlock>("ReinstallQuestion")!.Text);
                Assert.True(confirm.IsEnabled);

                view.FindControl<Button>("CancelReinstall")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(confirmation.IsVisible);
                Assert.True(excluded.ExcludedFromDownloadAll);
                Assert.Equal(2, UpdateManager.Repos.Count);

                UpdateManager.Repos.Clear();
                reinstall.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("No repositories to reinstall.", view.FindControl<TextBlock>("ReinstallQuestion")!.Text);
                Assert.False(confirm.IsEnabled);
            }
            finally
            {
                UpdateManager.Repos.Clear();
                foreach (Repo repo in previous) UpdateManager.Repos.Add(repo);
            }
            return Task.CompletedTask;
        }, CancellationToken.None);
    }
}
