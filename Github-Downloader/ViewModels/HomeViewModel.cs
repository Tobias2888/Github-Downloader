using System.Collections.ObjectModel;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;

namespace Github_Downloader.ViewModels;

public class HomeViewModel : ViewModelBase
{
    public ObservableCollection<Repo> Repos => UpdateManager.Repos;
}