using GithubDownloader.Core.Repositories;
using GithubDownloader.Core.Installation;
using System.Net;
using System.Text.Json;
using GithubDownloader.Core.Models;
using LoggerLib;

namespace GithubDownloader.Core.GitHub;

/// <summary>Retrieves repository metadata and selects releases without owning application state.</summary>
internal sealed class ReleaseService(Func<string, Task<HttpResponseMessage?>> request)
{
    private readonly Func<string, Task<HttpResponseMessage?>> _request = request;
    public async Task<Repo?> AddRepo(string repoUrl)
    {
        string publisherName = "";
        string repoName = "";

        try
        {
            string[] values = repoUrl.Split("github.com/");
            string[] values2 = values[1].TrimEnd('/').Split("/");
            publisherName = values2[0];
            repoName = values2[1];
        }
        catch (Exception) {
            _ = Logger.LogW("Repository URL could not be parsed");
            return null;
        }

        return await AddRepo(publisherName, repoName);
    }

    public async Task<Repo?> AddRepo(string publisherName, string repoName)
    {
        string url = $"https://api.github.com/repos/{publisherName}/{repoName}/releases/latest";
        string repoUrl = $"https://api.github.com/repos/{publisherName}/{repoName}";

        _ = Logger.LogI("Adding repository");

        using HttpResponseMessage? httpRepoResponse = await _request(repoUrl);
        if (httpRepoResponse is not { IsSuccessStatusCode: true })
        {
            _ = Logger.LogE("Failed to fetch repository");
            if (httpRepoResponse != null)
            {
                _ = Logger.LogE(httpRepoResponse.StatusCode.ToString());
            }
            return null;
        }

        RepoResponse? repoResponse = JsonSerializer.Deserialize<RepoResponse>(await httpRepoResponse.Content.ReadAsStringAsync());
        if (repoResponse is null || string.IsNullOrWhiteSpace(repoResponse.full_name)) return null;

        Repo repo = new()
        {
            Url = url,
            Name = repoResponse.full_name,
            Description = repoResponse.description ?? "",
            AssetRule = AssetSelection.GetDefaultRule()
        };

        _ = Logger.LogI("Repository added");
        return repo;
    }

    public async Task UpdateRepoDetails(IEnumerable<Repo> repos)
    {
        _ = Logger.LogI("Updating repo-details");

        foreach (Repo repo in repos.ToArray())
        {
            using HttpResponseMessage? httpRepoResponse = await _request(repo.Url.Replace("/releases/latest", ""));
            if (httpRepoResponse is not { IsSuccessStatusCode: true })
            {
                _ = Logger.LogE("Failed to fetch repository details");
                if (httpRepoResponse != null)
                {
                    _ = Logger.LogE(httpRepoResponse.StatusCode.ToString());
                }
                continue;
            }

            RepoResponse? repoResponse = JsonSerializer.Deserialize<RepoResponse>(await httpRepoResponse.Content.ReadAsStringAsync());
            if (repoResponse is null || string.IsNullOrWhiteSpace(repoResponse.full_name))
            {
                _ = Logger.LogE("Failed to parse repository details");
                continue;
            }

            repo.Name = repoResponse.full_name;
            repo.Description = repoResponse.description ?? "";
            repo.GitHubLink = repoResponse.html_url;

            if (repo.AssetNames.Count == 0)
            {
                await SearchForUpdates(repo, _ => { }, true);
            }
        }
    }

    private Task<HttpResponseMessage?> RequestRepositoryData(string url) => _request(url);

    public Task<bool> SearchForUpdates(IEnumerable<Repo> repos, Action<string> statusText) =>
        SearchForUpdates(repos, statusText, RequestRepositoryData);

    internal async Task<bool> SearchForUpdates(IEnumerable<Repo> repos, Action<string> statusText,
        Func<string, Task<HttpResponseMessage?>> request)
    {
        Repo[] snapshot = repos.ToArray();
        _ = Logger.LogI($"Checking for updates (repositories {snapshot.Length})");
        int failed = 0;
        string? firstFailure = null;
        foreach (Repo repo in snapshot)
        {
            statusText($"Checking for {repo.Name}");
            if (!await SearchForUpdates(repo, statusText, request, true, reason => firstFailure ??= reason)) failed++;
        }
        string summary = $"Update check finished: {snapshot.Length - failed} succeeded, {failed} failed.";
        if (firstFailure is not null) summary += $" First error: {firstFailure}";
        if (failed > 0)
        {
            await Logger.LogW(summary);
            statusText(summary + " Previous release information was kept for failed checks.");
        }
        else
        {
            await Logger.LogI(summary);
            statusText(summary);
        }
        return failed == 0;
    }

    private enum ReleaseLookup
    {
        Found,
        NotFound,
        Failed
    }

    public Task<bool> SearchForUpdates(Repo repo, Action<string> statusText, bool multiDownload = false) =>
        SearchForUpdates(repo, statusText, RequestRepositoryData, multiDownload);

    internal async Task<bool> SearchForUpdates(Repo repo, Action<string> statusText,
        Func<string, Task<HttpResponseMessage?>> request, bool multiDownload = false, Action<string>? requestFailure = null)
    {
        _ = Logger.LogD("Checking repository for updates");

        if (!multiDownload)
        {
            statusText.Invoke($"Checking for {repo.Name}");
        }

        void ReportFailure(string reason)
        {
            requestFailure?.Invoke(reason);
            statusText($"Failed to check releases for {repo.Name}. {reason} Previous release information was kept.");
        }

        List<Response>? releases = await GetReleaseList(repo, request, ReportFailure);
        if (releases == null) return false;

        List<string> tags = ["latest"];
        tags.AddRange(releases.Where(release => !release.draft
            && (repo.IncludePrereleases || !release.prerelease)).Select(release => release.tag_name).Distinct());
        // A pin remains selectable even when it is outside the channel or no longer published.
        if (!tags.Contains(repo.TargetTag)) tags.Add(repo.TargetTag);
        repo.Tags = tags;

        (ReleaseLookup lookup, Response? response) = repo.TargetTag != "latest"
            ? await GetReleaseByUrl($"https://api.github.com/repos/{repo.Name}/releases/tags/{Uri.EscapeDataString(repo.TargetTag)}", request, ReportFailure)
            : (ReleaseLookup.Found, releases.Where(release => !release.draft
                && (repo.IncludePrereleases || !release.prerelease))
                .OrderByDescending(release => DateTimeOffset.TryParse(release.published_at, out DateTimeOffset published)
                    ? published : DateTimeOffset.MinValue).FirstOrDefault());
        if (lookup == ReleaseLookup.Failed) return false;

        if (response == null || response.draft)
        {
            _ = Logger.LogD("Repository has no available release");
            repo.HasRelease = false;
            repo.ReplaceAssets([], []);
            repo.Tag = "";
            repo.LatestChangelog = "";
            repo.ReleaseDate = "";
            statusText(repo.ReleaseStatus);
            return true;
        }

        Assets[] assets = response.assets ?? [];
        repo.ReplaceAssets(assets.Select(asset => asset.name), assets.Select(asset => asset.url));
        repo.LatestChangelog = response.body ?? "";
        repo.ReleaseDate = response.published_at ?? "";
        repo.HasRelease = true;
        // Set Tag last: it is a persisted property, so its change triggers SaveRepos()
        // and this writes the whole, fully-updated release state to disk.
        repo.Tag = response.tag_name;
        if (WindowsSelfUpdate.IsSelfUpdate(repo))
            WindowsSelfUpdate.ReconcileInstalledVersions([repo], AppInfo.Version);

        _ = Logger.LogD("Repository release information refreshed");
        return true;
    }

    private async Task<(ReleaseLookup Lookup, Response? Response)> GetReleaseByUrl(string url, Func<string, Task<HttpResponseMessage?>> request, Action<string> reportFailure)
    {
        using HttpResponseMessage? httpResponse = await request(url);
        if (httpResponse == null)
        {
            await ReportReleaseFailureAsync(httpResponse, reportFailure);
            return (ReleaseLookup.Failed, null);
        }

        if (httpResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return (ReleaseLookup.NotFound, null);
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            await ReportReleaseFailureAsync(httpResponse, reportFailure);
            return (ReleaseLookup.Failed, null);
        }

        return (ReleaseLookup.Found,
            JsonSerializer.Deserialize<Response>(await httpResponse.Content.ReadAsStringAsync()));
    }

    private async Task<List<Response>?> GetReleaseList(Repo repo, Func<string, Task<HttpResponseMessage?>> request, Action<string> reportFailure)
    {
        List<Response> releases = [];
        for (int page = 1; ; page++)
        {
            string releasesUrl = $"https://api.github.com/repos/{repo.Name}/releases?per_page=100"
                + (page == 1 ? "" : $"&page={page}");
            using HttpResponseMessage? httpResponse = await request(releasesUrl);
            if (httpResponse is not { IsSuccessStatusCode: true })
            {
                await ReportReleaseFailureAsync(httpResponse, reportFailure);
                return null;
            }

            List<Response>? items = JsonSerializer.Deserialize<List<Response>>(await httpResponse.Content.ReadAsStringAsync());
            if (items == null)
            {
                const string reason = "GitHub returned an invalid release list.";
                await Logger.LogW(reason);
                reportFailure(reason);
                return null;
            }
            releases.AddRange(items);
            // Search every page: GitHub's list order does not guarantee publication order.
            bool hasNext = httpResponse.Headers.TryGetValues("Link", out IEnumerable<string>? links)
                && links.Any(link => link.Contains("rel=\"next\"", StringComparison.Ordinal));
            if (!hasNext && items.Count < 100) return releases;
        }
    }

    private async Task ReportReleaseFailureAsync(HttpResponseMessage? response, Action<string> reportFailure)
    {
        string reason = await GitHubApiError.DescribeAsync(response);
        await Logger.LogW($"Release check failed: {reason} Previous release information was kept.");
        reportFailure(reason);
    }

}
