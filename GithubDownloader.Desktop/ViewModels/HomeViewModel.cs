using System.Collections.ObjectModel;
using GithubDownloader.Core;
using GithubDownloader.Core.Models;

namespace GithubDownloader.Desktop.ViewModels;

public class HomeViewModel : ViewModelBase
{
    public ObservableCollection<Repo> Repos => UpdateManager.Repos;
}