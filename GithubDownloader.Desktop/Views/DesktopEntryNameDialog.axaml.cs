using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GithubDownloader.Desktop.Views;

public partial class DesktopEntryNameDialog : Window
{
    private readonly Func<string, Task> _save;

    public DesktopEntryNameDialog() : this("", _ => Task.CompletedTask) { }

    public DesktopEntryNameDialog(string name, Func<string, Task> save)
    {
        InitializeComponent();
        _save = save;
        NameInput.Text = name;
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void Save_OnClick(object? sender, RoutedEventArgs e)
    {
        SaveName.IsEnabled = false;
        SaveError.IsVisible = false;
        try
        {
            await _save(NameInput.Text ?? "");
            Close();
        }
        catch (Exception exception)
        {
            SaveError.Text = exception.Message;
            SaveError.IsVisible = true;
        }
        finally { SaveName.IsEnabled = true; }
    }
}
