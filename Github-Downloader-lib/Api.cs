using System.Diagnostics;
using System.Net.Http.Headers;
using FileLib;
using LoggerLib;

namespace Github_Downloader_lib;

public static class Api
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Github-Downloader/{AppInfo.Version}");
        return client;
    }

    public static Task<HttpResponseMessage?> GetRequest(string url, string token = "") =>
        GetRequest(Client, url, token);

    internal static async Task<HttpResponseMessage?> GetRequest(HttpClient client, string url, string token = "",
        CancellationToken cancellationToken = default)
    {
        const int attempts = 2;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (attempt < attempts && (response.StatusCode == System.Net.HttpStatusCode.RequestTimeout
                    || (int)response.StatusCode >= 500))
                {
                    response.Dispose();
                    Logger.LogD("Retrying a temporary GitHub API failure");
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                return response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (attempt < attempts
                && exception is OperationCanceledException or HttpRequestException)
            {
                Logger.LogD("Retrying a GitHub API request after a timeout or connection failure");
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Logger.LogE(exception is OperationCanceledException
                    ? "GitHub API request timed out after retry"
                    : $"GitHub API request failed ({exception.GetType().Name})");
                return null;
            }
        }
        return null;
    }

    public static async Task DownloadFileAsync(string url, string outputPath, string token = "", IProgress<double>? progress = null,
        Action<DownloadProgress>? transferProgress = null, CancellationToken cancellationToken = default)
    {
        Logger.LogI("Asset download started");
        try
        {
            using HttpClient client = new();

            // GitHub still requires User-Agent
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Github-Downloader/{AppInfo.Version}");
        
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }
        
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

            using HttpResponseMessage response = await client.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken
            );
            response.EnsureSuccessStatusCode();
        
            long? totalBytes = response.Content.Headers.ContentLength;
        
            FileHelper.Create(outputPath);
            await using FileStream fs = File.OpenWrite(outputPath);
            Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        
            await CopyDownloadAsync(stream, fs, totalBytes, progress, transferProgress, cancellationToken);
            Logger.LogI("Asset download completed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogI("Asset download cancelled");
            throw;
        }
        catch (Exception exception)
        {
            Logger.LogE($"Asset download failed ({exception.GetType().Name})");
            throw;
        }
    }

    internal static async Task CopyDownloadAsync(Stream source, Stream destination, long? totalBytes,
        IProgress<double>? progress = null, Action<DownloadProgress>? transferProgress = null, CancellationToken cancellationToken = default)
    {
        DownloadProgressSampler sampler = new(totalBytes, snapshot =>
        {
            transferProgress?.Invoke(snapshot);
            if (snapshot.Percent is double percent) progress?.Report(percent);
        });
        transferProgress?.Invoke(new DownloadProgress(0, totalBytes is > 0 ? totalBytes : null, 0, null));
        Stopwatch stopwatch = Stopwatch.StartNew();
        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;
            sampler.Report(totalRead, stopwatch.Elapsed);
        }
        cancellationToken.ThrowIfCancellationRequested();
        sampler.Report(totalRead, stopwatch.Elapsed, completed: true);
    }
}
