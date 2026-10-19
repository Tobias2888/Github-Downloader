using System.Net;
using System.Text.Json;
using Github_Downloader.Models;
using Github_Downloader.Services;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

public sealed class ReleaseChannelTests
{
    [Fact]
    public async Task RepositoriesSelectTheirOwnChannelUsingMetadataAndPublicationTime()
    {
        Response[] releases =
        [
            Release("draft", "2026-10-19", draft: true),
            Release("nightly", "2026-10-18", prerelease: true),
            Release("alphabetical", "2026-10-16"),
            Release("999.0", "2026-10-15")
        ];
        Repo stable = NewRepo();
        Repo preview = NewRepo();
        preview.IncludePrereleases = true;
        Assert.True(await Check(stable, releases));
        Assert.True(await Check(preview, releases));
        Assert.Equal("alphabetical", stable.Tag);
        Assert.Equal("nightly", preview.Tag);
        Assert.DoesNotContain("nightly", stable.Tags);
        Assert.DoesNotContain("draft", preview.Tags);
        Assert.Equal("installed", stable.CurrentInstallTag);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FindsNewestPublicationAcrossAllPages(bool includePrereleases)
    {
        Repo repo = NewRepo();
        repo.IncludePrereleases = includePrereleases;
        Response[] first = Enumerable.Range(0, 100)
            .Select(index => Release($"older-{index}", "2026-10-01", prerelease: true)).ToArray();
        List<string> requests = [];
        Assert.True(await UpdateManager.SearchForUpdates(repo, _ => { }, url =>
        {
            requests.Add(url);
            return Task.FromResult<HttpResponseMessage?>(Json(url.Contains("page=2")
                ? new[] { Release("newest", "2026-10-18") } : first));
        }));
        Assert.Equal(2, requests.Count);
        Assert.Equal("newest", repo.Tag);
    }

    [Fact]
    public async Task FollowsNextLinkEvenForShortPages()
    {
        Repo repo = NewRepo();
        int requests = 0;
        Assert.True(await UpdateManager.SearchForUpdates(repo, _ => { }, url =>
        {
            requests++;
            HttpResponseMessage response = Json(new[] { Release(requests == 1 ? "old" : "new", requests == 1 ? "2026-10-01" : "2026-10-18") });
            if (requests == 1) response.Headers.Add("Link", "<https://api.github.com/repos/owner/repo/releases?per_page=100&page=2>; rel=\"next\"");
            return Task.FromResult<HttpResponseMessage?>(response);
        }));
        Assert.Equal(2, requests);
        Assert.Equal("new", repo.Tag);
    }

    [Theory]
    [InlineData(false, "nightly")]
    [InlineData(true, "")]
    public async Task ExplicitTagOverridesChannelButDraftsAreExcluded(bool draft, string expected)
    {
        Repo repo = NewRepo();
        repo.TargetTag = "feature/nightly";
        List<string> requests = [];
        Assert.True(await UpdateManager.SearchForUpdates(repo, _ => { }, url =>
        {
            requests.Add(url);
            return Task.FromResult<HttpResponseMessage?>(url.Contains("/tags/")
                ? Json(Release("nightly", "2026-10-18", prerelease: true, draft: draft))
                : Json(Array.Empty<Response>()));
        }));
        Assert.Equal("feature/nightly", repo.TargetTag);
        Assert.Equal(expected, repo.Tag);
        Assert.Contains(requests, url => url.EndsWith("/tags/feature%2Fnightly"));
    }

    [Fact]
    public async Task MissingPinNeverFallsBackToLatest()
    {
        Repo repo = NewRepo();
        repo.TargetTag = "deleted";
        Assert.True(await UpdateManager.SearchForUpdates(repo, _ => { }, url =>
            Task.FromResult<HttpResponseMessage?>(url.Contains("/tags/")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(new[] { Release("stable", "2026-10-18") }))));
        Assert.Equal("deleted", repo.TargetTag);
        Assert.False(repo.HasRelease);
        Assert.Contains("deleted", repo.ReleaseStatus);
    }

    [Fact]
    public async Task StableChannelWithOnlyPreReleasesClearsOfferedVersionAndAssets()
    {
        Repo repo = NewRepo();
        repo.Tag = "cached";
        repo.ReplaceAssets(["cached.zip"], ["url"]);
        Assert.True(await Check(repo, [Release("nightly", "2026-10-18", prerelease: true)]));
        Assert.False(repo.HasRelease);
        Assert.Empty(repo.Tag);
        Assert.Empty(repo.AssetNames);
        Assert.Contains("Stable releases only", repo.ReleaseStatus);
        Assert.Equal("installed", repo.CurrentInstallTag);
    }

    [Fact]
    public async Task FailureOnLaterPagePreservesPreviousRelease()
    {
        Repo repo = NewRepo();
        repo.Tag = "cached";
        Assert.False(await UpdateManager.SearchForUpdates(repo, _ => { }, url =>
            Task.FromResult<HttpResponseMessage?>(url.Contains("page=2")
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json(Enumerable.Range(0, 100).Select(index => Release(index.ToString(), "2026-10-18"))))));
        Assert.Equal("cached", repo.Tag);
    }

    [Fact]
    public void ChannelChangeInvalidatesOfferedAssetsWithoutChangingInstalledVersionOrPin()
    {
        Repo repo = NewRepo();
        repo.Tag = "cached";
        repo.ReplaceAssets(["cached.zip"], ["url"]);
        repo.IncludePrereleases = true;
        Assert.False(repo.HasRelease);
        Assert.Empty(repo.SelectedAssetUrl);
        Assert.Equal("installed", repo.CurrentInstallTag);
        repo.TargetTag = "pinned";
        repo.Tag = "pinned";
        repo.IncludePrereleases = false;
        Assert.Equal("pinned", repo.Tag);
        Assert.Equal("pinned", repo.TargetTag);
    }

    [Fact]
    public void LegacyConfigurationsDefaultToStableAndChannelRoundTrips()
    {
        Repo legacy = JsonSerializer.Deserialize<Repo>("""{"Name":"owner/repo","Url":"url"}""")!;
        Assert.False(legacy.IncludePrereleases);
        legacy.IncludePrereleases = true;
        legacy.TargetTag = "nightly";
        legacy.Tag = "nightly";
        Repo restored = JsonSerializer.Deserialize<Repo>(JsonSerializer.Serialize(legacy))!;
        Assert.True(restored.IncludePrereleases);
        Assert.Equal("nightly", restored.TargetTag);
        Assert.Equal("nightly", restored.Tag);
    }

    [Fact]
    public async Task BatchAndManualChecksUseTheSameChannelForNotifications()
    {
        Repo stable = NewRepo();
        Repo preview = NewRepo();
        preview.Name = "owner/preview";
        preview.IncludePrereleases = true;
        Response[] releases = [Release("nightly", "2026-10-18", prerelease: true)];
        Assert.True(await UpdateManager.SearchForUpdates(new[] { stable, preview }, _ => { },
            _ => Task.FromResult<HttpResponseMessage?>(Json(releases))));
        Assert.Empty(stable.Tag);
        Assert.Equal("nightly", preview.Tag);
        List<string> notifications = [];
        NotificationService service = new(() => new NotificationSettings(), message =>
        {
            notifications.Add(message);
            return Task.FromResult(true);
        });
        await service.NotifyUpdatesAvailableAsync([stable, preview]);
        Assert.Equal(new[] { "1 update is available" }, notifications);
        preview.IncludePrereleases = false;
        Assert.True(await Check(preview, releases));
        await service.NotifyUpdatesAvailableAsync([stable, preview]);
        Assert.Single(notifications);
    }

    private static Repo NewRepo() => new()
    {
        Name = "owner/repo", Url = "https://api.github.com/repos/owner/repo/releases/latest",
        CurrentInstallTag = "installed"
    };

    private static Response Release(string tag, string published, bool prerelease = false, bool draft = false) => new()
    {
        tag_name = tag, published_at = published, prerelease = prerelease, draft = draft,
        assets = [], body = "notes"
    };

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value))
    };

    private static Task<bool> Check(Repo repo, Response[] releases) => UpdateManager.SearchForUpdates(repo,
        _ => { }, _ => Task.FromResult<HttpResponseMessage?>(Json(releases)));
}
