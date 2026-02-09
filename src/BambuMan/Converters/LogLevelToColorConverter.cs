using System.Globalization;
using LogLevel = BambuMan.Shared.Enums.LogLevel;

namespace BambuMan.Converters
{
    public class LogLevelToColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not LogLevel level)
                return Colors.Gray;

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

            return level switch
            {
                LogLevel.None or LogLevel.Trace or LogLevel.Debug =>
                    isDark ? Color.FromArgb("#9E9E9E") : Color.FromArgb("#696969"),
                LogLevel.Information =>
                    isDark ? Color.FromArgb("#E6E1E5") : Color.FromArgb("#1C1B1F"),
                LogLevel.Warning =>
                    isDark ? Color.FromArgb("#FFB74D") : Color.FromArgb("#FF8C00"),
                LogLevel.Success =>
                    isDark ? Color.FromArgb("#81C784") : Color.FromArgb("#006400"),
                LogLevel.Error or LogLevel.Critical =>
                    isDark ? Color.FromArgb("#EF9A9A") : Color.FromArgb("#800000"),
                _ => Colors.Gray
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
