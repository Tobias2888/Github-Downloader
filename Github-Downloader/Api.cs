using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Avalonia;
using FileLib;

namespace Github_Downloader;

public static class Api
{
    public static async Task<HttpResponseMessage> GetRequest(string url, string token = "")
    {
        HttpClient client = new();

        if (!string.IsNullOrEmpty(token))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }
        
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Github-Downloader/1.0");

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url);
        }
        catch (Exception)
        {
            Console.WriteLine($"Invalid url: {url}");
            response = null!;
        }

        return response;
    }
    
    public static async Task DownloadFileAsync(string url, string outputPath, string token = "", IProgress<double>? progress = null)
    {
        using HttpClient client = new();

        // GitHub still requires User-Agent
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Github-Downloader/1.0");
        
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