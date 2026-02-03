using Avalonia;
using Avalonia.Controls;
using Github_Downloader.ViewModels;

namespace Github_Downloader.Views;

public partial class RepoDetailsView : UserControl
{
    private readonly RepoDetailsViewModel _repoDetailsViewModel;
    
    public RepoDetailsView()
    {
        InitializeComponent();
        _repoDetailsViewModel = ((App)Application.Current!).RepoDetailsViewModel;
        DataContext = _repoDetailsViewModel;
    }
}