using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Github_Downloader.Models;
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
    public void OldSettingsKeepPlatformDefaultAndCustomRulesRoundTrip()
    {
        AppSettings settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Equal(AssetSelection.GetDefaultRule(), settings.DefaultAssetRule);
        settings.DefaultAssetRule = "*{linux,Linux}*\n! *arm64*";
        AppSettings restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(settings.DefaultAssetRule, restored.DefaultAssetRule);
        settings.DefaultAssetRule = "";
        restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Empty(restored.DefaultAssetRule);
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
