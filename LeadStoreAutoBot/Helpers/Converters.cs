using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LeadStoreAutoBot.Helpers;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Inverse { get; set; }

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool v && v;
        if (Inverse) b = !b;
        // Если параметр "enabled" — возвращаем bool для IsEnabled.
        if (parameter is string p && p == "enabled") return b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value as string ?? "";
        return status switch
        {
            "created" => Application.Current.Resources["YellowBrush"],
            "skipped" => Application.Current.Resources["RedBrush"],
            "duplicate" => new SolidColorBrush(Color.FromRgb(255, 182, 193)),
            "no_site" => Application.Current.Resources["Accent2Brush"],
            "error" => Application.Current.Resources["RedBrush"],
            _ => Brushes.Transparent,
        };
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}
