using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SoundboardApp.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class PadColorConverter : IValueConverter
{
    private static readonly Color[] Palette =
    [
        Color.FromRgb(232, 145, 58),
        Color.FromRgb(56, 168, 140),
        Color.FromRgb(72, 138, 204),
        Color.FromRgb(196, 92, 110),
        Color.FromRgb(168, 132, 74),
        Color.FromRgb(120, 110, 180),
        Color.FromRgb(90, 160, 100),
        Color.FromRgb(200, 110, 70)
    ];

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var index = value is int i ? Math.Abs(i) % Palette.Length : 0;
        return new SolidColorBrush(Palette[index]);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
