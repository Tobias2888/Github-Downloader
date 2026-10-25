using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Github_Downloader.Models;
using Github_Downloader.Views;
using Github_Downloader.ViewModels;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

[Collection("Repository dragging")]
public sealed class SettingsViewTests
{
    private readonly RepositoryDragApplication _application;
    public SettingsViewTests(RepositoryDragApplication application) => _application = application;

    [Theory]
    [InlineData(2, 1, true)]
    [InlineData(2, 0, true)]
    [InlineData(null, 2, true)]
    [InlineData(2, 3, false)]
    [InlineData(2, 2, false)]
    [InlineData(2, null, false)]
    [InlineData(null, null, false)]
    public async Task RetentionWarningAppearsOnlyForLowerLimitsAndDraftChangesInvalidateConfirmation(int? saved, int? proposed, bool warning)
    {
        await _application.Dispatch(() =>
        {
            App app = (App)Application.Current!;
            AppSettings previous = app.MainViewModel.AppSettings;
            try
            {
                app.MainViewModel.AppSettings = new() { LocalInstallerVersionsToKeep = saved };
                SettingsView view = new();
                SettingsViewModel model = Assert.IsType<SettingsViewModel>(view.DataContext);
                model.LocalInstallerRetentionMode = proposed.HasValue ? 0 : 1;
                if (proposed.HasValue) model.LocalInstallerVersionsToKeep = proposed.Value;
                Assert.Equal(warning, view.FindControl<TextBlock>("LocalInstallerRetentionWarning")!.IsVisible);
                if (warning)
                {
                    model.SaveLocalInstallerRetention();
                    Assert.True(model.ShowLocalInstallerRetentionConfirmation);
                    model.LocalInstallerVersionsToKeep++;
                    Assert.False(model.ShowLocalInstallerRetentionConfirmation);
                    model.ConfirmLocalInstallerRetention();
                    Assert.Equal(saved, app.MainViewModel.AppSettings.LocalInstallerVersionsToKeep);
                }
            }
            finally { app.MainViewModel.AppSettings = previous; }
            return Task.CompletedTask;
        }, CancellationToken.None);
    }

    [Fact]
    public void OldSettingsKeepPlatformDefaultAndCustomRulesRoundTrip()
    {
        AppSettings settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Equal(AssetSelection.GetDefaultRule(), settings.DefaultAssetRule);
        Assert.Equal(2, settings.LocalInstallerVersionsToKeep);
        settings.DefaultAssetRule = "*{linux,Linux}*\n! *arm64*";
        AppSettings restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(settings.DefaultAssetRule, restored.DefaultAssetRule);
        settings.DefaultAssetRule = "";
        restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Empty(restored.DefaultAssetRule);
    }

    [Fact]
    public async Task LocalInstallerLimitSavesZeroAndUnlimitedAndPrunesOnlyAfterApply()
    {
        await _application.Dispatch(() =>
        {
            App app = (App)Application.Current!;
            AppSettings previousSettings = app.MainViewModel.AppSettings;
            LocalAppStore previousApps = LocalApps.Store;
            FieldInfo settingsPath = typeof(AppSettings).GetField("_appSettingsFilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
            string previousPath = (string)settingsPath.GetValue(null)!;
            string root = Path.Combine(Path.GetTempPath(), "ghd-retention-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "appsettings.json");
            try
            {
                settingsPath.SetValue(null, path);
                app.MainViewModel.AppSettings = new();
                LocalApps.Store = new(Path.Combine(root, "local-apps.json"));
                List<LocalInstallerVersion> versions = [];
                for (int version = 1; version <= 3; version++)
                {
                    string installer = Path.Combine(root, "local-installers", $"app-v{version}.zip");
                    Directory.CreateDirectory(Path.GetDirectoryName(installer)!);
                    File.WriteAllText(installer, "saved installer");
                    versions.Add(new(installer, DateTimeOffset.UtcNow.AddMinutes(version)));
                }
                LocalApps.Store.Save(new()
                {
                    Id = "example", FileName = "app-v3.zip", InstallationName = "local/example",
                    Kind = LocalAppKind.Archive, InstallerPath = versions[2].Path, InstallerVersions = versions
                });
                SettingsView view = new();
                ComboBox mode = view.FindControl<ComboBox>("LocalInstallerRetentionModeSelector")!;
                NumericUpDown limit = view.FindControl<NumericUpDown>("LocalInstallerVersionLimit")!;
                Button save = view.FindControl<Button>("SaveLocalInstallerRetention")!;
                Assert.Equal(0, mode.SelectedIndex);
                Assert.True(limit.IsEffectivelyEnabled);
                Assert.Equal(2m, limit.Value);
                mode.SelectedIndex = 0;
                limit.Value = 2;
                Assert.True(limit.IsEffectivelyEnabled);
                Assert.All(versions, version => Assert.True(File.Exists(version.Path)));
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(2, AppSettings.Load().LocalInstallerVersionsToKeep);
                Assert.Equal(2, LocalInstallerRetention.LoadLimit(path));
                Assert.Equal(2, Assert.Single(LocalApps.Store.Read()).InstallerVersions.Count);
                Assert.False(File.Exists(versions[0].Path));
                app.MainViewModel.AppSettings = AppSettings.Load();
                SettingsView restored = new();
                Assert.Equal(0, restored.FindControl<ComboBox>("LocalInstallerRetentionModeSelector")!.SelectedIndex);
                Assert.Equal(2m, restored.FindControl<NumericUpDown>("LocalInstallerVersionLimit")!.Value);

                mode.SelectedIndex = 1;
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(AppSettings.Load().LocalInstallerVersionsToKeep);
                Assert.Equal(2, Assert.Single(LocalApps.Store.Read()).InstallerVersions.Count);
                mode.SelectedIndex = 0;
                limit.Value = 0;
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(view.FindControl<TextBlock>("LocalInstallerRetentionWarning")!.IsVisible);
                Assert.True(view.FindControl<Border>("LocalInstallerRetentionConfirmation")!.IsVisible);
                Assert.Null(AppSettings.Load().LocalInstallerVersionsToKeep);
                Assert.Equal(2, Assert.Single(LocalApps.Store.Read()).InstallerVersions.Count);
                view.FindControl<Button>("CancelLocalInstallerRetention")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(view.FindControl<Border>("LocalInstallerRetentionConfirmation")!.IsVisible);
                Assert.Equal(2, Assert.Single(LocalApps.Store.Read()).InstallerVersions.Count);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                view.FindControl<Button>("ConfirmLocalInstallerRetention")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, AppSettings.Load().LocalInstallerVersionsToKeep);
                LocalApp remaining = Assert.Single(LocalApps.Store.Read());
                Assert.True(remaining.IsInstalled);
                Assert.False(remaining.CanReinstall);
                Assert.Empty(remaining.InstallerVersions);
                Assert.All(versions, version => Assert.False(File.Exists(version.Path)));
                Assert.Contains("Saved", view.FindControl<TextBlock>("LocalInstallerRetentionStatus")!.Text);
            }
            finally
            {
                app.MainViewModel.AppSettings = previousSettings;
                LocalApps.Store = previousApps;
                settingsPath.SetValue(null, previousPath);
                Directory.Delete(root, true);
            }
            return Task.CompletedTask;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DefaultRuleEditorSavesChangesAndAllowsManualSelection()
    {
        await _application.Dispatch(() =>
        {
            App app = (App)Application.Current!;
            AppSettings previousSettings = app.MainViewModel.AppSettings;
            FieldInfo settingsPath = typeof(AppSettings).GetField("_appSettingsFilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
            string previousPath = (string)settingsPath.GetValue(null)!;
            string temporaryPath = Path.Combine(Path.GetTempPath(), $"asset-rule-settings-{Guid.NewGuid()}.json");
            try
            {
                settingsPath.SetValue(null, temporaryPath);
                app.MainViewModel.AppSettings = new();
                SettingsView view = new();
                TextBox editor = view.FindControl<TextBox>("TbxDefaultAssetRule")!;
                Assert.Equal(AssetSelection.GetDefaultRule(), editor.Text);
                editor.Text = "*.zip\n! *arm64*";
                Assert.Equal(editor.Text, app.MainViewModel.AppSettings.DefaultAssetRule);
                Assert.Equal(editor.Text, AppSettings.Load().DefaultAssetRule);
                Assert.Equal(editor.Text, new SettingsView().FindControl<TextBox>("TbxDefaultAssetRule")!.Text);
                editor.Text = "";
                Assert.Empty(AppSettings.Load().DefaultAssetRule);
            }
            finally
            {
                app.MainViewModel.AppSettings = previousSettings;
                settingsPath.SetValue(null, previousPath);
                File.Delete(temporaryPath);
            }
            return Task.CompletedTask;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NotificationFrequencyEditorPersistsModeAndInterval()
    {
        await _application.Dispatch(() =>
        {
            App app = (App)Application.Current!;
            AppSettings previousSettings = app.MainViewModel.AppSettings;
            FieldInfo settingsPath = typeof(AppSettings).GetField("_appSettingsFilePath", BindingFlags.NonPublic | BindingFlags.Static)!;
            string previousPath = (string)settingsPath.GetValue(null)!;
            string temporaryPath = Path.Combine(Path.GetTempPath(), $"notification-settings-{Guid.NewGuid()}.json");
            try
            {
                settingsPath.SetValue(null, temporaryPath);
                app.MainViewModel.AppSettings = new();
                SettingsView view = new();
                ComboBox mode = view.FindControl<ComboBox>("UpdateNotificationModeSelector")!;
                NumericUpDown interval = view.FindControl<NumericUpDown>("UpdateReminderInterval")!;
                Assert.Equal(0, mode.SelectedIndex);
                Assert.False(interval.IsEffectivelyEnabled);
                mode.SelectedIndex = 1;
                interval.Value = 30;
                Assert.True(interval.IsEffectivelyEnabled);
                Assert.True(AppSettings.Load().Notifications.RepeatUpdateNotifications);
                Assert.Equal(30, AppSettings.Load().Notifications.UpdateReminderIntervalMinutes);
                SettingsView restored = new();
                Assert.Equal(1, restored.FindControl<ComboBox>("UpdateNotificationModeSelector")!.SelectedIndex);
                Assert.Equal(30m, restored.FindControl<NumericUpDown>("UpdateReminderInterval")!.Value);
                mode.SelectedIndex = 0;
                Assert.False(AppSettings.Load().Notifications.RepeatUpdateNotifications);
                Assert.False(interval.IsEffectivelyEnabled);
            }
            finally
            {
                app.MainViewModel.AppSettings = previousSettings;
                settingsPath.SetValue(null, previousPath);
                File.Delete(temporaryPath);
            }
            return Task.CompletedTask;
        }, CancellationToken.None);
    }

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
