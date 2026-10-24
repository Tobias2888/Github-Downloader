using Github_Downloader_lib;
using Xunit;

namespace Github_Downloader.Tests;

internal static class TestStorage
{
    internal static string DirectoryPath { get; private set; } = "";

    internal static void Initialize()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "github-downloader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        Environment.SetEnvironmentVariable("GITHUB_DOWNLOADER_DATA_DIR", DirectoryPath);
    }
}

public sealed class TestStorageTests
{
    [Fact]
    public void ApplicationStorageIsIsolatedFromUserData()
    {
        Assert.Equal(TestStorage.DirectoryPath, FileManager.AppdataPath);
        Assert.Equal(Path.Combine(TestStorage.DirectoryPath, "repos.json"), FileManager.ReposConfigFilePath);
        Assert.Equal(Path.Combine(TestStorage.DirectoryPath, "cache"), FileManager.CachePath);
        Assert.Equal(Path.Combine(TestStorage.DirectoryPath, "app-images"), FileManager.AppImagesPath);
    }
}
