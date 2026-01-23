using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using FileLib;

namespace Github_Downloader;

public class Api
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
        catch (Exception e)
        {
            Console.WriteLine($"Invalid url: {url}");
            response = null;
        }

        return response;
    }
    
    public static async Task DownloadFileAsync(string url, string outputPath, string token = "")
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

        FileHelper.Create(outputPath);
        await using FileStream fs = File.OpenWrite(outputPath);
        await response.Content.CopyToAsync(fs);
    }
}