using System;
using System.Net.Http;
using System.Threading.Tasks;

class Program {
    static async Task Main() {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Github-Downloader/1.0");
        try {
            var res = await client.GetAsync("https://api.github.com/repos/BonitoCat/Github-Downloader/releases/latest");
            Console.WriteLine((int)res.StatusCode);
        } catch (Exception ex) {
            Console.WriteLine(ex.Message);
        }
    }
}
