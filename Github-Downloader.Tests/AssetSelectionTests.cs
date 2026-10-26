using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Github_Downloader;
using Github_Downloader.Views;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

[Collection("Repository dragging")]
public sealed class AssetSelectionTests
{
    private readonly RepositoryDragApplication _application;

    public AssetSelectionTests(RepositoryDragApplication application) => _application = application;

    [Theory]
    [InlineData("app-v2-linux-x64.AppImage", "*linux*x64*.AppImage", true)]
    [InlineData("app-v2-linux-arm64.AppImage", "*linux*x64*.AppImage", false)]
    [InlineData("release.zip", "release.zip", true)]
    [InlineData("releaseXzip", "release.zip", false)]
    [InlineData("release.zip", "Release.zip", false)]
    [InlineData("v2.zip", "v?.zip", true)]
    [InlineData("v12.zip", "v?.zip", false)]
    [InlineData("a[1]+(x).zip", "a[1]+(x).zip", true)]
    [InlineData("abcabc.zip", "*abc.zip", true)]
    [InlineData("abc", "a**?*", true)]
    [InlineData("abc", "*b", false)]
    [InlineData("", "*", true)]
    [InlineData("", "?", false)]
    [InlineData("release-amd64.deb", "*{x64,amd64,x86_64}*.deb", true)]
    [InlineData("release-arm64.deb", "*{x64,amd64,x86_64}*.deb", false)]
    [InlineData("release-x64.deb.sha256", "*{x64,amd64,x86_64}*.deb", false)]
    [InlineData("file{literal}.zip", "file{literal}.zip", true)]
    [InlineData("file{a,}.zip", "file{a,}.zip", true)]
    [InlineData("file{a,b.zip", "file{a,b.zip", true)]
    [InlineData("filea+.zip", "file{a+,b}.zip", true)]
    [InlineData("fileaa.zip", "file{a+,b}.zip", false)]
    [InlineData("file*.zip", "file{*,?}.zip", true)]
    public void GlobsMatchCompleteFilenamesWithWildcardsAndLiteralAlternatives(string name, string rule, bool expected)
        => Assert.Equal(expected, AssetSelection.Matches(name, rule));

    [Theory]
    [InlineData("app-x64.zip", "*.zip\n! *arm64*", true)]
    [InlineData("app-arm64.zip", "*.zip\n!*arm64*", false)]
    [InlineData("app-arm64.zip", "!*arm64*\n*.zip", false)]
    [InlineData("app.exe", "*.zip\n*.exe", false)]
    [InlineData("app-x64.zip", "*.zip\n*x64*", true)]
    [InlineData("app-arm64.zip", "*.zip\n*x64*", false)]
    [InlineData("app-x64.zip", "*.zip *x64*", true)]
    [InlineData("app-x64.exe", "*.zip *x64*", false)]
    [InlineData("app-arm64.zip", "*.zip *x64*", false)]
    [InlineData("app-x64.zip", "*.zip *x64* ! *arm64*", true)]
    [InlineData("app-x64.zip", "*.zip *x64* !*x64*", false)]
    [InlineData("app-x64.zip", "! *x64* *.zip *x64*", false)]
    [InlineData("app-x64.zip", "\t*.zip  \n*x64*\r\n! \t *arm64* ", true)]
    [InlineData("app.tar.gz", "*.zip\n*.exe", false)]
    [InlineData("app-x64.zip", "\r\n *.zip \r\n\r\n ! *arm64* \r\n", true)]
    [InlineData("app-amd64.zip", "*{x64,amd64}.zip\n!*{arm64,amd64}.zip", false)]
    [InlineData("app-x64.zip", "*.zip\n!*arm64*\n!app-?64.zip", false)]
    [InlineData("app.zip", "*.zip\n!APP.zip", true)]
    [InlineData("app.zip", "!*.exe", false)]
    [InlineData("app.zip", "\n \r\n", false)]
    [InlineData("app.zip", "*.zip\n!", true)]
    [InlineData("app.zip", "*.zip\n!*", false)]
    public void RulesRequireEveryInclusionAndRejectEveryExclusion(string name, string rule, bool expected)
    {
        Assert.Equal(expected, AssetSelection.Matches(name, rule));
        Assert.Equal(expected, AssetSelection.FindMatches([name], rule).Count == 1);
    }

    [Fact]
    public void ExclusionsResolveVersionedVariantsAndSurviveExportImport()
    {
        Repo repo = NewRepo();
        repo.AssetRule = "*_x64_setup.exe\n! *_lt12_x64_setup.exe";
        repo.ReplaceAssets(["qbittorrent_5.3.0_x64_setup.exe", "qbittorrent_5.3.0_lt12_x64_setup.exe"],
            ["standard-url", "lt12-url"]);
        Assert.Equal(["qbittorrent_5.3.0_x64_setup.exe"], repo.AssetRuleMatches);
        Assert.Equal(0, repo.DownloadAssetIndex);
        Assert.Equal("standard-url", repo.SelectedAssetUrl);

        Repo restored = JsonSerializer.Deserialize<Repo>(JsonSerializer.Serialize(repo))!;
        Assert.Equal(repo.AssetRule, restored.AssetRule);
        restored.ReplaceAssets(["qbittorrent_5.4.0_lt12_x64_setup.exe", "qbittorrent_5.4.0_x64_setup.exe"],
            ["new-lt12-url", "new-standard-url"]);
        Assert.Equal(1, restored.DownloadAssetIndex);
        Assert.Equal("new-standard-url", restored.SelectedAssetUrl);
        restored.ReplaceAssets(["qbittorrent_5.4.0_lt12_x64_setup.exe"], ["new-lt12-url"]);
        Assert.Empty(restored.AssetRuleMatches);
        Assert.Empty(restored.SelectedAssetUrl);
        Assert.Equal(-1, restored.DownloadAssetIndex);
    }

    [Theory]
    [InlineData(Architecture.X64, "app-x64-v2.deb", true)]
    [InlineData(Architecture.X64, "app-amd64.deb", true)]
    [InlineData(Architecture.X64, "app-x86_64.deb", true)]
    [InlineData(Architecture.X64, "app-arm64.deb", false)]
    [InlineData(Architecture.X64, "app-x64.msi", false)]
    [InlineData(Architecture.X64, "app-x64.deb.sha256", false)]
    [InlineData(Architecture.X64, "app.deb", false)]
    [InlineData(Architecture.Arm64, "app-arm64.deb", true)]
    [InlineData(Architecture.Arm64, "app-aarch64.deb", true)]
    [InlineData(Architecture.Arm64, "app-amd64.deb", false)]
    [InlineData(Architecture.X86, "app-x86.deb", true)]
    [InlineData(Architecture.X86, "app-i386.deb", true)]
    [InlineData(Architecture.X86, "app-x86_64.deb", false)]
    [InlineData(Architecture.Arm, "app-armhf.deb", true)]
    [InlineData(Architecture.Arm, "app-armv7l.deb", true)]
    [InlineData(Architecture.Arm, "app-arm64.deb", false)]
    public void DebianDefaultsRequireMatchingArchitectureAndInstaller(Architecture architecture, string asset, bool expected)
    {
        string rule = AssetSelection.GetDefaultRule(OSPlatform.Linux, architecture,
            "NAME=Linux Mint\nID=linuxmint\nID_LIKE=\"ubuntu debian\"\n");
        Assert.Equal(expected, AssetSelection.Matches(asset, rule));
        Assert.Equal(expected, AssetSelection.FindMatches([asset], rule).Count == 1);
    }

    [Theory]
    [InlineData("ID=debian", ".deb")]
    [InlineData("ID=ubuntu", ".deb")]
    [InlineData("ID=fedora", ".rpm")]
    [InlineData("ID=rocky\nID_LIKE=\"rhel centos fedora\"", ".rpm")]
    [InlineData("ID=opensuse-tumbleweed", ".rpm")]
    [InlineData("ID=arch", ".AppImage")]
    [InlineData("NAME=Debian but ID is unknown\nID=unknown", ".AppImage")]
    [InlineData("", ".AppImage")]
    public void LinuxDefaultsUseTheDistributionInstaller(string release, string extension)
        => Assert.EndsWith(extension, AssetSelection.GetDefaultRule(OSPlatform.Linux, Architecture.X64, release));

    [Fact]
    public void DefaultsUseTheHostOperatingSystemAndSupportWindowsAndMacInstallers()
    {
        Assert.Equal("*{x64,amd64,x86_64}*.msi", AssetSelection.GetDefaultRule(OSPlatform.Windows, Architecture.X64));
        Assert.Equal("*{arm64,aarch64}*.dmg", AssetSelection.GetDefaultRule(OSPlatform.OSX, Architecture.Arm64));
        Assert.Empty(AssetSelection.GetDefaultRule(OSPlatform.Create("unsupported"), Architecture.X64));
        Assert.Empty(AssetSelection.GetDefaultRule(OSPlatform.Linux, Architecture.Wasm));
        Assert.NotEmpty(AssetSelection.GetDefaultRule());
    }

    [Fact]
    public void DefaultRuleFollowsNewReleasesAndRemainsEditableAfterExport()
    {
        Repo repo = NewRepo();
        repo.AssetRule = AssetSelection.GetDefaultRule(OSPlatform.Linux, Architecture.X64, "ID=debian");
        repo.ReplaceAssets(["app-arm64.deb", "v1-amd64.deb", "app-x64.msi"], ["arm", "v1", "windows"]);
        Assert.Equal("v1", repo.SelectedAssetUrl);
        Repo restored = JsonSerializer.Deserialize<Repo>(JsonSerializer.Serialize(repo))!;
        restored.ReplaceAssets(["v2-x86_64.deb", "app-arm64.deb"], ["v2", "arm"]);
        Assert.Equal("v2", restored.SelectedAssetUrl);
        restored.ReplaceAssets(["v2-x64.deb", "v2-amd64.deb"], ["first", "second"]);
        Assert.Equal(2, restored.AssetRuleMatches.Count);
        Assert.Empty(restored.SelectedAssetUrl);
        restored.AssetRule = "";
        restored.DownloadAssetIndex = 1;
        Assert.Equal("second", restored.SelectedAssetUrl);
    }

    [Fact]
    public void ManualSelectionKeepsIndexAcrossRenamesReorderingAndMissingAssets()
    {
        Repo repo = NewRepo();
        repo.AssetNames = ["windows.zip", "linux.zip"];
        repo.DownloadAssetIndex = 1;
        string json = JsonSerializer.Serialize(repo);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("AssetName", out JsonElement _));
        Assert.Equal(1, document.RootElement.GetProperty("DownloadAssetIndex").GetInt32());
        Repo restored = JsonSerializer.Deserialize<Repo>(json)!;
        restored.ReplaceAssets(["v2-linux.zip", "v2-windows.zip"], ["linux-url", "windows-url"]);
        Assert.Equal(1, restored.DownloadAssetIndex);
        Assert.Equal("windows-url", restored.SelectedAssetUrl);
        restored.ReplaceAssets(["windows.zip"], ["windows-url"]);
        Assert.Equal(-1, restored.DownloadAssetIndex);
        Assert.Empty(restored.SelectedAssetUrl);
        Assert.Contains("out of range", restored.AssetSelectionStatus);
        restored = JsonSerializer.Deserialize<Repo>(JsonSerializer.Serialize(restored))!;
        restored.ReplaceAssets(["first.zip", "second.zip"], ["first-url", "second-url"]);
        Assert.Equal(1, restored.DownloadAssetIndex);
        Assert.Equal("second.zip", restored.SelectedAssetName);
    }

    [Fact]
    public void RulesFollowVersionedAssetsAndRequireExactlyOneMatch()
    {
        Repo repo = NewRepo();
        repo.AssetRule = "*linux*.zip";
        repo.ReplaceAssets(["v1-windows.zip", "v1-linux.zip"], ["windows-url", "v1-url"]);
        Assert.Equal("v1-linux.zip", repo.SelectedAssetName);
        repo.ReplaceAssets(["v2-linux.zip", "v2-windows.zip"], ["v2-url", "windows-url"]);
        Assert.Equal("v2-url", repo.SelectedAssetUrl);
        repo.ReplaceAssets(["linux-x64.zip", "linux-arm64.zip"], ["x64-url", "arm64-url"]);
        Assert.Equal(2, repo.AssetRuleMatches.Count);
        Assert.Equal(-1, repo.DownloadAssetIndex);
        Assert.Empty(repo.SelectedAssetName);
        Assert.Empty(repo.SelectedAssetUrl);
        Assert.Contains("2 assets match", repo.AssetSelectionStatus);
        repo.ReplaceAssets(["windows.zip"], ["windows-url"]);
        Assert.Empty(repo.AssetRuleMatches);
        Assert.Contains("No assets match", repo.AssetSelectionStatus);
    }

    [Fact]
    public void RuleCanBeClearedToRestoreManualSelectionAndReplacedByAnotherSelection()
    {
        Repo repo = NewRepo();
        repo.AssetNames = ["manual.zip", "linux.zip"];
        repo.DownloadAssetIndex = 0;
        repo.AssetRule = "*linux*";
        Assert.Equal(1, repo.DownloadAssetIndex);
        // A binding echoes the computed index; that must preserve the rule and manual index.
        repo.DownloadAssetIndex = 1;
        Assert.Equal("*linux*", repo.AssetRule);
        repo.AssetRule = "";
        Assert.Equal("manual.zip", repo.SelectedAssetName);
        repo.DownloadAssetIndex = 1;
        Assert.Equal("linux.zip", repo.AssetName);
        repo.DownloadAssetIndex = -1; // Transient ComboBox reset is ignored.
        Assert.Equal("linux.zip", repo.SelectedAssetName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SavedIndexIsIndependentOfJsonPropertyOrder(bool indexFirst)
    {
        string index = "\"DownloadAssetIndex\":1";
        string names = "\"AssetNames\":[\"windows.zip\",\"linux.zip\"]";
        string json = "{\"Name\":\"owner/repo\",\"Url\":\"url\"," +
            (indexFirst ? index + "," + names : names + "," + index) + "}";
        Repo repo = JsonSerializer.Deserialize<Repo>(json)!;
        Assert.Equal("linux.zip", repo.AssetName);
        repo.ReplaceAssets(["linux.zip", "windows.zip"], ["linux-url", "windows-url"]);
        Assert.Equal("windows.zip", repo.SelectedAssetName);
        Assert.Equal(1, repo.DownloadAssetIndex);
    }

    [Theory]
    [InlineData("{\"DownloadAssetIndex\":8,\"AssetNames\":[\"other.zip\"]}", -1, "")]
    [InlineData("{\"DownloadAssetIndex\":0,\"AssetNames\":[]}", 0, "new-url")]
    public void SavedIndexResolvesWhenItIsInRangeAgain(string legacy, int expectedIndex, string expectedUrl)
    {
        string json = legacy.Insert(1, "\"Name\":\"owner/repo\",\"Url\":\"url\",");
        Repo repo = JsonSerializer.Deserialize<Repo>(json)!;
        repo.ReplaceAssets(["new.zip"], ["new-url"]);
        Assert.Equal(expectedIndex, repo.DownloadAssetIndex);
        Assert.Equal(expectedUrl, repo.SelectedAssetUrl);
    }

    [Fact]
    public void RuleAndSavedManualIndexSurviveExportImportAndMissingReleases()
    {
        Repo repo = NewRepo();
        repo.AssetNames = ["manual.zip", "v1-linux.zip"];
        repo.DownloadAssetIndex = 0;
        repo.AssetRule = "*linux*.zip";
        repo.ReplaceAssets([], []);
        Repo restored = JsonSerializer.Deserialize<Repo>(JsonSerializer.Serialize(repo))!;
        Assert.Equal("*linux*.zip", restored.AssetRule);
        Assert.Equal(0, restored.SavedDownloadAssetIndex);
        restored.ReplaceAssets(["v2-linux.zip", "manual.zip"], ["linux-url", "manual-url"]);
        Assert.Equal("v2-linux.zip", restored.SelectedAssetName);
        restored.AssetRule = "";
        Assert.Equal("v2-linux.zip", restored.SelectedAssetName);
    }

    [Fact]
    public void RuleOnlyConfigurationDoesNotGainAnImplicitManualSelectionAfterRestart()
    {
        Repo repo = NewRepo();
        repo.AssetRule = "*linux*";
        repo.ReplaceAssets(["linux.zip", "windows.zip"], ["linux-url", "windows-url"]);
        Assert.Empty(repo.AssetName);
        Repo restored = JsonSerializer.Deserialize<Repo>(JsonSerializer.Serialize(repo))!;
        Assert.Equal("linux.zip", restored.SelectedAssetName);
        Assert.Empty(restored.AssetName);
        restored.AssetRule = "";
        Assert.Equal(-1, restored.DownloadAssetIndex);
        Assert.Empty(restored.SelectedAssetName);
    }

    [Fact]
    public void SavedIndexHasPriorityOverLegacyFilename()
    {
        Repo repo = JsonSerializer.Deserialize<Repo>("""
            {"Name":"owner/repo","Url":"url","AssetName":"linux.zip","DownloadAssetIndex":0,
             "AssetNames":["windows.zip","linux.zip"],"AssetRule":""}
            """)!;
        Assert.Equal(0, repo.DownloadAssetIndex);
        Assert.Equal("windows.zip", repo.SelectedAssetName);
    }

    [Fact]
    public async Task ReleaseRefreshUsesTheRuleAndKeepsItWhenNoReleaseExists()
    {
        Repo repo = NewRepo();
        repo.AssetNames = ["v1-windows.zip", "v1-linux.zip"];
        repo.DownloadAssetIndex = 1;
        repo.AssetRule = "*linux*.zip";
        Assert.True(await UpdateManager.SearchForUpdates(repo, _ => { }, url =>
            Task.FromResult<HttpResponseMessage?>(JsonResponse("""
                [{"tag_name":"v2","body":"notes","published_at":"2026-10-16","assets":[
                  {"name":"v2-linux.zip","url":"linux-url"},
                  {"name":"v2-windows.zip","url":"windows-url"}]}]
                """))));
        Assert.Equal("v2-linux.zip", repo.SelectedAssetName);
        Assert.Equal("linux-url", repo.SelectedAssetUrl);
        Assert.Equal("v2", repo.Tag);
        Assert.True(await UpdateManager.SearchForUpdates(repo, _ => { }, url =>
            Task.FromResult<HttpResponseMessage?>(url.EndsWith("/tags?per_page=100") || url.EndsWith("/releases?per_page=100")
                ? JsonResponse("[]") : new HttpResponseMessage(HttpStatusCode.NotFound))));
        Assert.False(repo.HasAssets);
        Assert.Equal("*linux*.zip", repo.AssetRule);
        Assert.Equal(1, repo.SavedDownloadAssetIndex);
    }

    [Theory]
    [InlineData("missing*", false)]
    [InlineData("*", false)]
    [InlineData("missing*", true)]
    [InlineData("*", true)]
    public async Task UnresolvedRulesBlockDownloadsAndReportTheReason(string rule, bool bulk)
    {
        Repo repo = NewRepo();
        repo.ReplaceAssets(["one.zip", "two.zip"], ["invalid-url-one", "invalid-url-two"]);
        repo.AssetRule = rule;
        List<string> messages = [];
        bool success = bulk
            ? await UpdateManager.UpdateReposAsync(new[] { repo }, messages.Add, messages.Add)
            : await UpdateManager.UpdateRepo(repo, messages.Add, messages.Add);
        Assert.False(success);
        Assert.Equal("v1", repo.CurrentInstallTag);
        Assert.Contains(messages, message => message.Contains("Download blocked"));
    }

    [Fact]
    public async Task BulkUpdateContinuesPastUnresolvedRepositories()
    {
        Repo first = NewRepo();
        first.Name = "owner/first";
        first.AssetRule = "missing*";
        Repo second = NewRepo();
        second.Name = "owner/second";
        second.AssetRule = "missing*";
        List<string> messages = [];
        Assert.False(await UpdateManager.UpdateReposAsync(new[] { first, second }, messages.Add, _ => { }));
        Assert.Contains(messages, message => message.StartsWith("Skipped owner/first:"));
        Assert.Contains(messages, message => message.StartsWith("Skipped owner/second:"));
    }

    [Fact]
    public async Task DesktopDownloadSettingsFollowAssetSelectionAndSaveAnyway()
    {
        await _application.Dispatch(async () =>
        {
            Repo repo = NewRepo();
            repo.ReplaceAssets(["app.deb", "app.zip", "app.msi", "app.AppImage", "app.pkg.tar.zst"], ["deb", "zip", "msi", "appimage", "arch"]);
            repo.DownloadAssetIndex = 0;
            repo.AutoExtract = true;
            repo.NewFileName = "custom-name";
            ((App)Application.Current!).RepoDetailsViewModel.Repo = repo;
            RepoDetailsView details = new();
            Window window = new() { Width = 1100, Height = 850, Content = details };
            window.Show();
            try
            {
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                await Task.Delay(50);
                Grid saveRow = details.FindControl<Grid>("StpSaveFileAnyway")!;
                ToggleSwitch save = details.FindControl<ToggleSwitch>("TglSaveFileAnyway")!;
                ToggleSwitch extract = details.FindControl<ToggleSwitch>("TglAutoExtract")!;
                StackPanel downloadPath = details.FindControl<StackPanel>("StpDownloadPath")!;
                TextBox rename = details.FindControl<TextBox>("TbxRenameFile")!;
                ComboBox selection = details.FindControl<ComboBox>("CobAsset")!;
                Assert.Equal(InstallationManager.CanInstallDebianPackages, saveRow.IsVisible);
                Assert.Equal(!InstallationManager.CanInstallDebianPackages, extract.IsEffectivelyVisible);
                Assert.Equal(!InstallationManager.CanInstallDebianPackages, downloadPath.IsVisible);

                save.IsChecked = true;
                Assert.True(repo.SaveFileAnyway);
                Assert.True(extract.IsEffectivelyVisible);
                Assert.True(downloadPath.IsVisible);
                Assert.True(rename.IsEffectivelyVisible);
                save.IsChecked = false;
                Assert.Equal(!InstallationManager.CanInstallDebianPackages, extract.IsEffectivelyVisible);

                selection.SelectedIndex = 1;
                Assert.False(saveRow.IsVisible);
                Assert.True(extract.IsEffectivelyVisible);
                Assert.True(downloadPath.IsVisible);
                Assert.True(rename.IsEffectivelyVisible);

                selection.SelectedIndex = 2;
                Assert.True(saveRow.IsVisible);
                Assert.False(extract.IsEffectivelyVisible);
                selection.SelectedIndex = 3;
                Assert.Equal(!RuntimeInformation.IsOSPlatform(OSPlatform.Linux), downloadPath.IsVisible);

                selection.SelectedIndex = 4;
                Assert.Equal(InstallationManager.CanInstallArchPackages, saveRow.IsVisible);
                Assert.Equal(!InstallationManager.CanInstallArchPackages, downloadPath.IsVisible);
                Assert.Equal(!InstallationManager.CanInstallArchPackages, extract.IsEffectivelyVisible);

                repo.AssetRule = "*.zip";
                Assert.False(saveRow.IsVisible);
                Assert.True(extract.IsEffectivelyVisible);
                Assert.True(downloadPath.IsVisible);
                repo.AssetRule = "*.deb";
                Assert.Equal(InstallationManager.CanInstallDebianPackages, saveRow.IsVisible);
                Assert.Equal(!InstallationManager.CanInstallDebianPackages, extract.IsEffectivelyVisible);
                Assert.Equal(!InstallationManager.CanInstallDebianPackages, downloadPath.IsVisible);
                repo.SaveFileAnyway = true;
                Assert.True(save.IsChecked);
                Assert.True(extract.IsEffectivelyVisible);
                Assert.True(downloadPath.IsVisible);
                Assert.True(repo.AutoExtract);
                Assert.Equal("custom-name", repo.NewFileName);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DesktopDetailsEditsRulesAndPreviewsMatches()
    {
        await _application.Dispatch(async () =>
        {
            Repo repo = NewRepo();
            repo.ReplaceAssets(["linux-x64.zip", "linux-arm64.zip", "windows.zip"], ["x64", "arm64", "windows"]);
            ((App)Application.Current!).RepoDetailsViewModel.Repo = repo;
            RepoDetailsView details = new();
            Window window = new() { Width = 1100, Height = 850, Content = details };
            window.Show();
            try
            {
                TextBox rule = details.FindControl<TextBox>("TbxAssetRule")!;
                ComboBox selection = details.FindControl<ComboBox>("CobAsset")!;
                TextBlock status = details.FindControl<TextBlock>("TbxAssetSelectionStatus")!;
                rule.Text = "*linux*";
                window.UpdateLayout();
                await Task.Delay(50);
                Assert.Equal("*linux*", repo.AssetRule);
                Assert.False(selection.IsEnabled);
                Assert.Contains("2 assets match", status.Text);
                Assert.True(rule.AcceptsReturn);
                rule.Text = "*linux*\n*.zip ! *arm64*";
                window.UpdateLayout();
                Assert.Equal("*linux*\n*.zip ! *arm64*", repo.AssetRule);
                Assert.Equal("linux-x64.zip", repo.SelectedAssetName);
                Assert.Contains("linux-x64.zip", status.Text);
                rule.Text = "";
                selection.SelectedIndex = 2;
                window.UpdateLayout();
                Assert.True(selection.IsEnabled);
                Assert.Equal("windows.zip", repo.AssetName);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DesktopComboBoxRefreshCannotOverwriteSavedSelectionOrRule()
    {
        await _application.Dispatch(async () =>
        {
            Repo repo = NewRepo();
            repo.AssetNames = ["windows.zip", "linux.zip"];
            repo.DownloadAssetIndex = 1;
            RepoEntryControl entry = new() { DataContext = repo };
            Window window = new() { Width = 1100, Height = 200, Content = entry };
            window.Show();
            try
            {
                repo.ReplaceAssets(["linux.zip", "windows.zip"], ["linux-url", "windows-url"]);
                window.UpdateLayout();
                await Task.Delay(50);
                Assert.Equal("windows.zip", repo.AssetName);
                Assert.Equal(1, repo.DownloadAssetIndex);
                repo.AssetRule = "*windows*";
                window.UpdateLayout();
                await Task.Delay(50);
                Assert.Equal("*windows*", repo.AssetRule);
                repo.ReplaceAssets(["other.zip"], ["other-url"]);
                window.UpdateLayout();
                await Task.Delay(50);
                Assert.Equal("*windows*", repo.AssetRule);
                Assert.Equal(-1, repo.DownloadAssetIndex);
                repo.AssetRule = "";
                window.UpdateLayout();
                await Task.Delay(50);
                Assert.Equal(1, repo.SavedDownloadAssetIndex);
                Assert.Empty(repo.SelectedAssetName);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static Repo NewRepo() => new()
    {
        Name = "owner/repo", Url = "https://api.github.com/repos/owner/repo/releases/latest",
        CurrentInstallTag = "v1", Tag = "v2"
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json)
    };
}
