using GithubDownloader.Core.Models;

namespace GithubDownloader.Desktop.ViewModels;

public class RepoDetailsViewModel : ViewModelBase
{
    public Repo? Repo { get; set; }
}