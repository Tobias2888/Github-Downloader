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

    public static async Task DownloadFileAsync(string url, string outputPath, string token = "", IProgress<double>? progress = null)
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
                HttpCompletionOption.ResponseHeadersRead
            );
            response.EnsureSuccessStatusCode();
        
            long? totalBytes = response.Content.Headers.ContentLength;
        
            FileHelper.Create(outputPath);
            await using FileStream fs = File.OpenWrite(outputPath);
            Stream stream = await response.Content.ReadAsStreamAsync();
        
            byte[] buffer = new byte[81920]; // 80 KB buffer
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;

                if (!totalBytes.HasValue || progress == null) continue;
            
                double percent = (double)totalRead / totalBytes.Value * 100;
                progress.Report(percent);
            }
            Logger.LogI("Asset download completed");
        }
        catch (Exception exception)
        {
            Logger.LogE($"Asset download failed ({exception.GetType().Name})");
            throw;
        }
    }
}
