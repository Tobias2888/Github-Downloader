using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Github_Downloader;

public class Api
{
    public static async Task<string> GetRequest(string url)
    {
        HttpClient client = new();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "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");
        
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Github-Downloader/1.0");
        
        string responseJson = await client.GetStringAsync(url);
        
        return responseJson;
    }
}