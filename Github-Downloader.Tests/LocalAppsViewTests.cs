using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Github_Downloader.Views;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

[Collection("Repository dragging")]
public sealed class LocalAppsViewTests
{
    private readonly RepositoryDragApplication _application;
    public LocalAppsViewTests(RepositoryDragApplication application) => _application = application;

    [Fact]
    public async Task LocalTabShowsPersistedAppsAndUsesLocalUninstallConfirmation()
    {
        string root = Path.Combine(Path.GetTempPath(), "ghd-local-apps-ui-" + Guid.NewGuid().ToString("N"));
        LocalAppStore previous = LocalApps.Store;
        LocalApps.Store = new(Path.Combine(root, "local-apps.json"));
        LocalApp app = new()
        {
            Id = "example", FileName = "Example.AppImage", InstallationName = "local/example",
            Kind = LocalAppKind.AppImage, AppImageId = "local-example", InstalledAt = DateTimeOffset.UtcNow
        };
        LocalApps.Store.Save(app);
        try
        {
            await _application.Dispatch(async () =>
            {
                HomeView home = new();
                Window window = new() { Content = home, Width = 1120, Height = 750 };
                window.Show();
                try
                {
                    window.UpdateLayout();
                    TabStrip tabs = home.FindControl<TabStrip>("AppTabs")!;
                    Button update = home.FindControl<Button>("BtnUpdateAll")!;
                    double? tabsY = tabs.TranslatePoint(default, window)?.Y + tabs.Bounds.Height / 2;
                    double? updateY = update.TranslatePoint(default, window)?.Y + update.Bounds.Height / 2;
                    Assert.NotNull(tabsY);
                    Assert.NotNull(updateY);
                    Assert.InRange(Math.Abs(tabsY.Value - updateY.Value), 0, 1);
                    window.CaptureRenderedFrame()?.Save("/tmp/github-downloader-repository-header.png");
                    tabs.SelectedIndex = 1;
                    window.UpdateLayout();
                    await Task.Delay(40);
                    LocalAppsView panel = home.FindControl<LocalAppsView>("LocalAppsPanel")!;
                    panel.Refresh();
                    Assert.True(home.FindControl<TextBox>("TbxUrl")!.IsEffectivelyVisible);
                    Assert.True(panel.IsEffectivelyVisible);
                    Assert.Equal(app.Id, Assert.IsType<LocalApp>(Assert.Single(panel.FindControl<ItemsControl>("AppsList")!.Items)).Id);
                    LocalAppsView standalone = new();
                    bool requested = false;
                    standalone.InstallRequested += (_, _) => requested = true;
                    standalone.FindControl<Button>("InstallLocal")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(requested);
                    UninstallRepoDialog dialog = new(app);
                    Assert.False(dialog.FindControl<TextBlock>("RepositoryNotice")!.IsVisible);
                    Assert.Contains("AppImage", dialog.FindControl<TextBlock>("UninstallDetails")!.Text);
                    Assert.Equal("Example", dialog.FindControl<TextBlock>("AppName")!.Text);
                    Assert.Equal(OperatingSystem.IsLinux(), dialog.FindControl<Button>("ConfirmUninstall")!.IsEnabled);
                    Assert.Single(LocalApps.Store.Read());
                    window.UpdateLayout();
                    window.CaptureRenderedFrame()?.Save("/tmp/github-downloader-local-apps.png");
                    LocalApps.Store.Remove(app.Id);
                    panel.Refresh();
                    Assert.Empty(panel.FindControl<ItemsControl>("AppsList")!.Items);
                    Assert.Equal("No local apps installed yet.", panel.FindControl<TextBlock>("ListStatus")!.Text);
                }
                finally { window.Close(); }
            }, CancellationToken.None);
        }
        finally
        {
            LocalApps.Store = previous;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
