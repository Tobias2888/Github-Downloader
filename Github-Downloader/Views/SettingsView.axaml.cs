using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Github_Downloader.Enums;
using Github_Downloader.ViewModels;

namespace Github_Downloader.Views;

public partial class SettingsView : UserControl
{
    private readonly MainViewModel _mainViewModel;
    
    public SettingsView()
    {
        InitializeComponent();
        _mainViewModel = ((App) Application.Current).MainViewModel;
        DataContext = new SettingsViewModel();
    }

    private void ImgBack_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _mainViewModel.SwitchPage(ViewNames.Home);
    }

    private void NotificationTimePicker_OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        TimePickerPresenter? presenter = e.NameScope.Find<TimePickerPresenter>("PART_PickerPresenter");
        if (presenter is null) return;

        presenter.TemplateApplied += (_, args) =>
        {
            DateTimePickerPanel? hours = args.NameScope.Find<DateTimePickerPanel>("PART_HourSelector");
            DateTimePickerPanel? minutes = args.NameScope.Find<DateTimePickerPanel>("PART_MinuteSelector");
            if (hours is not null) new TimePickerScrollAnimation(hours);
            if (minutes is not null) new TimePickerScrollAnimation(minutes);
        };
    }
}
