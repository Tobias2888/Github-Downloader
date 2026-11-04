using GithubDownloader.Core.Repositories;
using System.Collections.ObjectModel;
using GithubDownloader.Core;
using GithubDownloader.Core.Models;
using Xunit;

namespace GithubDownloader.Tests;

public sealed class RepositoryCatalogTests
{
    [Fact]
    public void WatchingTwiceDoesNotDuplicateSaves()
    {
        int saves = 0;
        using RepositoryCatalog catalog = new(() => saves++);
        Repo repo = new() { Name = "owner/repo", Url = "https://example.com/repo" };
        catalog.Repos.Add(repo);
        catalog.Watch();
        catalog.Watch();
        repo.NewFileName = "renamed.zip";
        Assert.Equal(1, saves);
    }

    [Fact]
    public void ClearingAndReplacingCollectionsDetachesOldRepositories()
    {
        int saves = 0;
        using RepositoryCatalog catalog = new(() => saves++);
        Repo removed = new() { Name = "owner/removed", Url = "https://example.com/removed" };
        catalog.Repos.Add(removed);
        catalog.Watch();
        catalog.Repos.Clear();
        int afterClear = saves;
        removed.NewFileName = "removed.zip";
        Assert.Equal(afterClear, saves);
        ObservableCollection<Repo> previous = catalog.Repos;
        Repo current = new() { Name = "owner/current", Url = "https://example.com/current" };
        catalog.Repos = [current];
        previous.Add(removed);
        Assert.Equal(afterClear, saves);
        current.NewFileName = "current.zip";
        Assert.Equal(afterClear + 1, saves);
    }
}
