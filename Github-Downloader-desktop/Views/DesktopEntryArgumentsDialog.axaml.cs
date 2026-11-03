using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Github_Downloader.Views;

public partial class DesktopEntryArgumentsDialog : Window
{
    private readonly Func<string, Task> _save;

    public DesktopEntryArgumentsDialog() : this("", _ => Task.CompletedTask) { }

    public DesktopEntryArgumentsDialog(string arguments, Func<string, Task> save)
    {
        InitializeComponent();
        _save = save;
        ArgumentsInput.Text = arguments;
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close();

    private async void Save_OnClick(object? sender, RoutedEventArgs e)
    {
        SaveArguments.IsEnabled = false;
        SaveError.IsVisible = false;
        try
        {
            await _save(ArgumentsInput.Text ?? "");
            Close();
        }
        catch (Exception exception)
        {
            SaveError.Text = exception.Message;
            SaveError.IsVisible = true;
        }
        finally { SaveArguments.IsEnabled = true; }
    }
}
