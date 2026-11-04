using System.Globalization;
using System.Net;
using System.Text.Json;

namespace GithubDownloader.Core.GitHub;

/// <summary>Creates actionable diagnostics without exposing response bodies, URLs, or credentials.</summary>
internal static class GitHubApiError
{
    internal static async Task<string> DescribeAsync(HttpResponseMessage? response)
    {
        if (response is null)
            return "No response from GitHub. Check your connection; the request may have timed out.";

        int status = (int)response.StatusCode;
        bool primaryLimit = HeaderNumber(response, "x-ratelimit-remaining") == 0;
        bool limited = response.StatusCode == HttpStatusCode.TooManyRequests
            || response.StatusCode == HttpStatusCode.Forbidden
                && (primaryLimit || response.Headers.Contains("Retry-After") || await HasRateLimitMessageAsync(response));
        if (limited)
        {
            long? limit = HeaderNumber(response, "x-ratelimit-limit");
            string quota = primaryLimit && limit is > 0 ? $"; 0 of {limit.Value} requests remaining" : "";
            string retry = RetryAdvice(response, primaryLimit);
            return $"GitHub API rate limit exceeded (HTTP {status}{quota}). {retry} "
                + "For a higher limit on unauthenticated requests, configure a GitHub personal access token.";
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "GitHub authentication failed (HTTP 401). Check whether your personal access token is invalid, expired, or revoked.",
            HttpStatusCode.Forbidden => "GitHub denied access (HTTP 403). Check your token permissions and repository access.",
            HttpStatusCode.NotFound => "GitHub repository or releases were not found (HTTP 404). Check the repository name and token access for private repositories.",
            _ when status >= 500 => $"GitHub is temporarily unavailable (HTTP {status}). Try again later.",
            _ => $"GitHub API request failed (HTTP {status})."
        };
    }

    private static string RetryAdvice(HttpResponseMessage response, bool primaryLimit)
    {
        if (response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? values))
        {
            string value = values.FirstOrDefault() ?? "";
            if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds) && seconds >= 0)
                return $"Retry after {seconds.ToString(CultureInfo.InvariantCulture)} seconds.";
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset retry))
                return $"Retry after {retry.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz} (local time).";
        }
        long? reset = HeaderNumber(response, "x-ratelimit-reset");
        if (primaryLimit && reset.HasValue)
        {
            try
            {
                DateTimeOffset local = DateTimeOffset.FromUnixTimeSeconds(reset.Value).ToLocalTime();
                return $"Retry after {local:yyyy-MM-dd HH:mm:ss zzz} (local time).";
            }
            catch (ArgumentOutOfRangeException) { /* Ignore a malformed server timestamp. */ }
        }
        return "Wait at least one minute before retrying; if still limited, wait longer.";
    }

    private static long? HeaderNumber(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            && long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out long number)
            ? number : null;
    }

    private static async Task<bool> HasRateLimitMessageAsync(HttpResponseMessage response)
    {
        try
        {
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.ValueKind == JsonValueKind.Object
                && body.RootElement.TryGetProperty("message", out JsonElement message)
                && message.ValueKind == JsonValueKind.String
                && message.GetString()!.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException or IOException)
        {
            return false;
        }
    }
}
