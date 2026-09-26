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
            Timeout = TimeSpan.FromSeconds(10),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Github-Downloader/{AppInfo.Version}");
        return client;
    }

    public static async Task<HttpResponseMessage?> GetRequest(string url, string token = "")
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        try
        {
            return await Client.SendAsync(request);
        }
        catch (Exception)
        {
            Console.WriteLine($"Invalid url: {url}");
            Logger.LogI("Invalid url");
            return null;
        }
    }
    
    public static async Task DownloadFileAsync(string url, string outputPath, string token = "", IProgress<double>? progress = null)
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
    }
}