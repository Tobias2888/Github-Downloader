using Avalonia.Data.Converters;
using System;
using System.Globalization;

public class ReleaseDateStringToFormattedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return "Released at: unknown";
        
        if (DateTimeOffset.TryParse(s, out DateTimeOffset dto))
        {
            return "Released at: " + dto.ToLocalTime().ToString("dd. MMMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        }

        return "Released at: unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}