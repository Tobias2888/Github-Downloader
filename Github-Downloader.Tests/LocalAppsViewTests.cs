using Avalonia;
using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyArchUninstallDialogRequiresAValidPackageName(bool local)
    {
        if (!InstallationManager.CanInstallArchPackages) return;
        await _application.Dispatch(() =>
        {
            Repo repo = new() { Url = "", Name = "owner/arch", CurrentInstallTag = "v1" };
            repo.ReplaceAssets(["app.pkg.tar.zst"], ["https://example.invalid/package"]);
            repo.DownloadAssetIndex = 0;
            LocalApp app = new()
            {
                Id = "legacy-arch", FileName = "app.pkg.tar.zst", InstallationName = "local/arch", Kind = LocalAppKind.Arch
            };
            UninstallRepoDialog dialog = local ? new(app) : new(repo);
            dialog.Show();
            try
            {
                dialog.UpdateLayout();
                Assert.True(dialog.FindControl<StackPanel>("PackageFields")!.IsVisible);
                Button confirm = dialog.FindControl<Button>("ConfirmUninstall")!;
                TextBox package = dialog.FindControl<TextBox>("PackageName")!;
                Assert.False(confirm.IsEnabled);
                package.Text = "--all";
                Dispatcher.UIThread.RunJobs();
                Assert.False(confirm.IsEnabled);
                package.Text = "actual-package";
                Dispatcher.UIThread.RunJobs();
                Assert.True(confirm.IsEnabled);
                package.Text = "";
                Dispatcher.UIThread.RunJobs();
                Assert.False(confirm.IsEnabled);
            }
            finally { dialog.Close(); }
            return Task.CompletedTask;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SavedVersionPickerCancelsWithoutChangesAndInstallsTheChosenArchive()
    {
        string root = Path.Combine(Path.GetTempPath(), "ghd-version-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LocalAppStore previous = LocalApps.Store;
        LocalApps.Store = new(Path.Combine(root, "local-apps.json"));
        string destination = Path.Combine(root, "application");
        try
        {
            for (int version = 1; version <= 2; version++)
            {
                string source = Path.Combine(root, $"app-v{version}.zip");
                using (ZipArchive archive = ZipFile.Open(source, ZipArchiveMode.Create))
                {
                    using StreamWriter writer = new(archive.CreateEntry("application.txt").Open());
                    writer.Write($"version {version}");
                }
                Assert.True(await LocalFileInstaller.InstallAsync(source, _ => { }, _ => { }, destination, null, Path.Combine(root, "cache"), 2));
            }
            await _application.Dispatch(async () =>
            {
                LocalAppsView panel = new();
                Window window = new() { Content = panel, Width = 1120, Height = 750 };
                window.Show();
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    window.UpdateLayout();
                    Button choose = Assert.Single(panel.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Install version…"));
                    Assert.True(choose.IsEffectivelyVisible);
                    choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    LocalInstallerVersionDialog dialog = Assert.Single(window.OwnedWindows.OfType<LocalInstallerVersionDialog>());
                    ListBox versions = dialog.FindControl<ListBox>("VersionsList")!;
                    Assert.Equal(2, versions.ItemCount);
                    Assert.False(dialog.FindControl<Button>("ConfirmInstall")!.IsEnabled);
                    dialog.FindControl<Button>("CancelInstall")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Assert.Equal("version 2", File.ReadAllText(Path.Combine(destination, "application.txt")));

                    choose = Assert.Single(panel.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Install version…"));
                    choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    dialog = Assert.Single(window.OwnedWindows.OfType<LocalInstallerVersionDialog>());
                    versions = dialog.FindControl<ListBox>("VersionsList")!;
                    versions.SelectedIndex = 1;
                    Assert.Equal("app-v1.zip", Assert.IsType<LocalInstallerVersion>(versions.SelectedItem).FileName);
                    Assert.True(dialog.FindControl<Button>("ConfirmInstall")!.IsEnabled);
                    dialog.FindControl<Button>("ConfirmInstall")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
                    while (Assert.Single(LocalApps.Store.Read()).FileName != "app-v1.zip" || ((App)Application.Current!).DownloadStatusViewModel.IsUpdating)
                        await Task.Delay(10, timeout.Token);
                    Assert.Equal("version 1", File.ReadAllText(Path.Combine(destination, "application.txt")));
                    Assert.Equal(2, Assert.Single(LocalApps.Store.Read()).InstallerVersions.Count);
                }
                finally { window.Close(); }
            }, CancellationToken.None);
        }
        finally
        {
            LocalApps.Store = previous;
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveIsSeparateFromUninstallAndRequiresConfirmation(bool installed)
    {
        string root = Path.Combine(Path.GetTempPath(), "ghd-local-remove-ui-" + Guid.NewGuid().ToString("N"));
        LocalAppStore previous = LocalApps.Store;
        LocalApps.Store = new(Path.Combine(root, "local-apps.json"));
        string saved = Path.Combine(root, "local-installers", "example", "example.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        File.WriteAllText(saved, "saved installer");
        LocalApp app = new()
        {
            Id = "example", FileName = "example.zip", InstallationName = "local/example",
            Kind = LocalAppKind.Archive, InstallerPath = saved, IsInstalled = installed,
            ExtractionDirectory = Path.Combine(LocalApps.Store.AppsPath, "example")
        };
        if (installed) Directory.CreateDirectory(app.ExtractionDirectory);
        LocalApps.Store.Save(app);
        try
        {
            await _application.Dispatch(async () =>
            {
                LocalAppsView panel = new();
                Window window = new() { Content = panel, Width = 1120, Height = 750 };
                window.Show();
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    panel.Refresh();
                    window.UpdateLayout();
                    Button[] buttons = panel.GetVisualDescendants().OfType<Button>().ToArray();
                    Button remove = Assert.Single(buttons, button => Equals(button.Content, "Remove"));
                    Assert.True(remove.IsEffectivelyVisible);
                    Assert.True(Assert.Single(buttons, button => Equals(button.Content, "Reinstall")).IsEffectivelyVisible);
                    Assert.Equal(installed && OperatingSystem.IsLinux(), Assert.Single(buttons, button => Equals(button.Content, "Uninstall")).IsVisible);

                    remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    UninstallRepoDialog dialog = Assert.Single(window.OwnedWindows.OfType<UninstallRepoDialog>());
                    Assert.Contains("saved installer", dialog.FindControl<TextBlock>("UninstallDetails")!.Text);
                    Assert.Single(LocalApps.Store.Read());
                    Assert.True(File.Exists(saved));
                    dialog.Close(null);
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Assert.Single(LocalApps.Store.Read());

                    remove = Assert.Single(panel.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Remove"));
                    remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    dialog = Assert.Single(window.OwnedWindows.OfType<UninstallRepoDialog>());
                    dialog.FindControl<Button>("ConfirmUninstall")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Assert.Empty(LocalApps.Store.Read());
                    Assert.False(File.Exists(saved));
                    Assert.Empty(panel.FindControl<ItemsControl>("AppsList")!.Items);
                }
                finally { window.Close(); }
            }, CancellationToken.None);
        }
        finally
        {
            LocalApps.Store = previous;
            Directory.Delete(root, true);
        }
    }

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
                    window.CaptureRenderedFrame()?.Save("/tmp/github-downloader-repository-header.png", PngBitmapEncoderOptions.Default);
                    tabs.SelectedIndex = 1;
                    window.UpdateLayout();
                    await Task.Delay(40);
                    LocalAppsView panel = home.FindControl<LocalAppsView>("LocalAppsPanel")!;
                    panel.Refresh();
                    Assert.True(home.FindControl<TextBox>("TbxUrl")!.IsEffectivelyVisible);
                    Assert.True(panel.IsEffectivelyVisible);
                    Assert.Equal(app.Id, Assert.IsType<LocalApp>(Assert.Single(panel.FindControl<ItemsControl>("AppsList")!.Items)).Id);
                    UninstallRepoDialog dialog = new(app);
                    Assert.False(dialog.FindControl<TextBlock>("RepositoryNotice")!.IsVisible);
                    Assert.Contains("AppImage", dialog.FindControl<TextBlock>("UninstallDetails")!.Text);
                    Assert.Equal("Example", dialog.FindControl<TextBlock>("AppName")!.Text);
                    Assert.Equal(OperatingSystem.IsLinux(), dialog.FindControl<Button>("ConfirmUninstall")!.IsEnabled);
                    Assert.Single(LocalApps.Store.Read());
                    window.UpdateLayout();
                    window.CaptureRenderedFrame()?.Save("/tmp/github-downloader-local-apps.png", PngBitmapEncoderOptions.Default);
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
