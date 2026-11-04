using GithubDownloader.Core.GitHub;
using System.Net;
using GithubDownloader.Core;
using GithubDownloader.Core.Models;
using Xunit;

namespace GithubDownloader.Tests;

public sealed class GitHubApiErrorTests
{
    [Fact]
    public async Task PrimaryLimitIncludesQuotaAndLocalResetTime()
    {
        using HttpResponseMessage response = new(HttpStatusCode.Forbidden);
        response.Headers.Add("x-ratelimit-remaining", "0");
        response.Headers.Add("x-ratelimit-limit", "60");
        response.Headers.Add("x-ratelimit-reset", "1793831049");
        string error = await GitHubApiError.DescribeAsync(response);
        Assert.Contains("rate limit exceeded (HTTP 403; 0 of 60 requests remaining)", error);
        DateTimeOffset reset = DateTimeOffset.FromUnixTimeSeconds(1793831049).ToLocalTime();
        Assert.Contains($"Retry after {reset:yyyy-MM-dd HH:mm:ss zzz} (local time)", error);
        Assert.Contains("personal access token", error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RetryAfterTakesPrecedenceOverPrimaryReset(HttpStatusCode status)
    {
        using HttpResponseMessage response = new(status);
        response.Headers.Add("x-ratelimit-remaining", "0");
        response.Headers.Add("x-ratelimit-reset", "1793831049");
        response.Headers.Add("Retry-After", "120");
        string error = await GitHubApiError.DescribeAsync(response);
        Assert.Contains("rate limit exceeded", error);
        Assert.Contains("Retry after 120 seconds", error);
        Assert.DoesNotContain("local time", error);
    }

    [Fact]
    public async Task SecondaryLimitUsesMessageWithoutExposingBody()
    {
        using HttpResponseMessage response = new(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"message\":\"You have exceeded a secondary rate limit. private-marker\"}")
        };
        response.Headers.Add("x-ratelimit-remaining", "4999");
        string error = await GitHubApiError.DescribeAsync(response);
        Assert.Contains("rate limit exceeded", error);
        Assert.Contains("Wait at least one minute", error);
        Assert.DoesNotContain("private-marker", error);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("999999999999999999")]
    public async Task MalformedResetDoesNotHideRateLimit(string reset)
    {
        using HttpResponseMessage response = new(HttpStatusCode.Forbidden);
        response.Headers.Add("x-ratelimit-remaining", "0");
        response.Headers.Add("x-ratelimit-reset", reset);
        string error = await GitHubApiError.DescribeAsync(response);
        Assert.Contains("rate limit exceeded", error);
        Assert.Contains("Wait at least one minute", error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authentication failed")]
    [InlineData(HttpStatusCode.Forbidden, "denied access")]
    [InlineData(HttpStatusCode.NotFound, "not found")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "temporarily unavailable")]
    public async Task OtherHttpFailuresHaveSpecificAdvice(HttpStatusCode status, string expected)
    {
        using HttpResponseMessage response = new(status) { Content = new StringContent("not JSON: private-marker") };
        string error = await GitHubApiError.DescribeAsync(response);
        Assert.Contains(expected, error);
        Assert.Contains($"HTTP {(int)status}", error);
        Assert.DoesNotContain("rate limit exceeded", error);
        Assert.DoesNotContain("private-marker", error);
    }

    [Fact]
    public async Task BatchSummaryIncludesFailureReasonAndPreservesCachedReleases()
    {
        Repo first = new() { Name = "owner/first", Url = "https://api.github.com/repos/owner/first", Tag = "cached" };
        Repo second = new() { Name = "owner/second", Url = "https://api.github.com/repos/owner/second", Tag = "cached" };
        List<string> messages = [];
        Assert.False(await UpdateManager.SearchForUpdates(new[] { first, second }, messages.Add, _ =>
        {
            HttpResponseMessage response = new(HttpStatusCode.Forbidden);
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("Retry-After", "60");
            return Task.FromResult<HttpResponseMessage?>(response);
        }));
        Assert.Contains("0 succeeded, 2 failed", messages[^1]);
        Assert.Contains("rate limit exceeded", messages[^1]);
        Assert.Contains("Retry after 60 seconds", messages[^1]);
        Assert.Equal("cached", first.Tag);
        Assert.Equal("cached", second.Tag);
    }

    [Fact]
    public async Task MissingResponseExplainsConnectionOrTimeout()
    {
        string error = await GitHubApiError.DescribeAsync(null);
        Assert.Contains("No response", error);
        Assert.Contains("timed out", error);
    }
}
