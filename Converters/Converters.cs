using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CDriveMigrator.Models;

namespace CDriveMigrator.Converters;

public class RiskLevelToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RiskLevel risk)
        {
            return risk switch
            {
                RiskLevel.Safe => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                RiskLevel.Low => new SolidColorBrush(Color.FromRgb(74, 222, 128)),
                RiskLevel.Medium => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                RiskLevel.High => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                RiskLevel.Critical => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))
            };
        }
        return new SolidColorBrush(Color.FromRgb(148, 163, 184));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class MigrationStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is MigrationStatus status)
        {
            return status switch
            {
                MigrationStatus.Completed => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                MigrationStatus.Failed => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                MigrationStatus.RolledBack => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                MigrationStatus.Moving or MigrationStatus.FixingReferences or
                MigrationStatus.CreatingJunction or MigrationStatus.Verifying
                    => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))
            };
        }
        return new SolidColorBrush(Color.FromRgb(148, 163, 184));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value == null;
        var invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        if (invert) isNull = !isNull;
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class RecommendationToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Recommendation rec)
        {
            return rec switch
            {
                Recommendation.Recommended => new SolidColorBrush(Color.FromRgb(34, 197, 94)),   // green
                Recommendation.Caution => new SolidColorBrush(Color.FromRgb(245, 158, 11)),      // amber
                Recommendation.NotRecommended => new SolidColorBrush(Color.FromRgb(239, 68, 68)), // red
                _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))
            };
        }
        return new SolidColorBrush(Color.FromRgb(148, 163, 184));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ProgressToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double progress && parameter is string maxStr && double.TryParse(maxStr, out var maxWidth))
        {
            return Math.Max(0, progress / 100.0 * maxWidth);
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ReferenceTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ReferenceType type)
        {
            return type switch
            {
                ReferenceType.RegistryValue => "🔑",
                ReferenceType.Shortcut => "🔗",
                ReferenceType.WindowsService => "⚙",
                ReferenceType.EnvironmentVariable => "📋",
                ReferenceType.ScheduledTask => "⏰",
                ReferenceType.ConfigFile => "📄",
                _ => "❓"
            };
        }
        return "❓";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
