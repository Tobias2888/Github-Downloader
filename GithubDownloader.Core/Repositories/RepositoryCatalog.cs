using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using GithubDownloader.Core.Models;
using LoggerLib;

namespace GithubDownloader.Core.Repositories;

/// <summary>Owns repository subscriptions and persistence notifications for one application session.</summary>
internal sealed class RepositoryCatalog(Action save) : IDisposable
{
    private ObservableCollection<Repo> _repos = [];
    private readonly HashSet<Repo> _watched = [];
    private bool _watching;
    private static readonly HashSet<string> PersistedProperties =
    [
        nameof(Repo.AssetName), nameof(Repo.AssetRule), nameof(Repo.ExcludedFromDownloadAll),
        nameof(Repo.TargetTag), nameof(Repo.IncludePrereleases), nameof(Repo.Tag), nameof(Repo.DownloadPath),
        nameof(Repo.SaveFileAnyway), nameof(Repo.AutoExtract), nameof(Repo.CreateArchiveLauncher),
        nameof(Repo.ArchiveInstallation), nameof(Repo.NewFileName), nameof(Repo.InstalledDebianPackage),
        nameof(Repo.InstalledArchPackage), nameof(Repo.CurrentInstallTag), nameof(Repo.LastDownloadTag)
    ];

    public ObservableCollection<Repo> Repos
    {
        get => _repos;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_repos, value)) return;
            bool resume = _watching;
            Dispose();
            _repos = value;
            if (resume) Watch();
        }
    }

    public void Watch()
    {
        if (_watching) return;
        _watching = true;
        _repos.CollectionChanged += CollectionChanged;
        SynchronizeSubscriptions();
    }

    private void SynchronizeSubscriptions()
    {
        foreach (Repo old in _watched.Where(repo => !_repos.Contains(repo)).ToArray())
        {
            old.PropertyChanged -= PropertyChanged;
            _watched.Remove(old);
        }
        foreach (Repo repo in _repos)
            if (_watched.Add(repo)) repo.PropertyChanged += PropertyChanged;
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        SynchronizeSubscriptions();
        _ = Logger.LogD($"Repository list changed: {args.Action}");
        save();
    }

    private void PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Repo.IsSelected)) _ = Logger.LogI("Repository selection changed");
        if (args.PropertyName is string name && PersistedProperties.Contains(name))
        {
            _ = Logger.LogD($"Repository setting changed: {name}");
            save();
        }
    }

    public void Dispose()
    {
        _repos.CollectionChanged -= CollectionChanged;
        foreach (Repo repo in _watched) repo.PropertyChanged -= PropertyChanged;
        _watched.Clear();
        _watching = false;
    }
}
