using CDriveMigrator.Converters;
using CDriveMigrator.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CDriveMigrator.Tests.Converters;

public class BoolToVisibilityConverterTests
{
    private readonly BoolToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsVisible()
    {
        var result = _converter.Convert(true, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_False_ReturnsCollapsed()
    {
        var result = _converter.Convert(false, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void ConvertBack_Visible_ReturnsTrue()
    {
        var result = _converter.ConvertBack(Visibility.Visible, typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(true, result);
    }

    [Fact]
    public void ConvertBack_Collapsed_ReturnsFalse()
    {
        var result = _converter.ConvertBack(Visibility.Collapsed, typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, result);
    }
}

public class InverseBoolConverterTests
{
    private readonly InverseBoolConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsFalse()
    {
        var result = _converter.Convert(true, typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, result);
    }

    [Fact]
    public void Convert_False_ReturnsTrue()
    {
        var result = _converter.Convert(false, typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Convert_NonBool_ReturnsOriginal()
    {
        var result = _converter.Convert("not a bool", typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal("not a bool", result);
    }

    [Fact]
    public void ConvertBack_True_ReturnsFalse()
    {
        var result = _converter.ConvertBack(true, typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(false, result);
    }

    [Fact]
    public void ConvertBack_False_ReturnsTrue()
    {
        var result = _converter.ConvertBack(false, typeof(bool), null!, CultureInfo.InvariantCulture);
        Assert.Equal(true, result);
    }
}

public class NullToVisibilityConverterTests
{
    private readonly NullToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_Null_ReturnsCollapsed()
    {
        var result = _converter.Convert(null!, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_NotNull_ReturnsVisible()
    {
        var result = _converter.Convert("something", typeof(Visibility), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_NullWithInvert_ReturnsVisible()
    {
        var result = _converter.Convert(null!, typeof(Visibility), "Invert", CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_NotNullWithInvert_ReturnsCollapsed()
    {
        var result = _converter.Convert("something", typeof(Visibility), "Invert", CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, result);
    }
}

public class ProgressToWidthConverterTests
{
    private readonly ProgressToWidthConverter _converter = new();

    [Fact]
    public void Convert_50Percent_ReturnsHalfWidth()
    {
        var result = _converter.Convert(50.0, typeof(double), "200", CultureInfo.InvariantCulture);
        Assert.Equal(100.0, result);
    }

    [Fact]
    public void Convert_100Percent_ReturnsFullWidth()
    {
        var result = _converter.Convert(100.0, typeof(double), "300", CultureInfo.InvariantCulture);
        Assert.Equal(300.0, result);
    }

    [Fact]
    public void Convert_0Percent_ReturnsZero()
    {
        var result = _converter.Convert(0.0, typeof(double), "200", CultureInfo.InvariantCulture);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Convert_NegativeValue_ReturnsZero()
    {
        var result = _converter.Convert(-10.0, typeof(double), "200", CultureInfo.InvariantCulture);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Convert_InvalidParameter_ReturnsZero()
    {
        var result = _converter.Convert(50.0, typeof(double), "notanumber", CultureInfo.InvariantCulture);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void Convert_NonDoubleValue_ReturnsZero()
    {
        var result = _converter.Convert("invalid", typeof(double), "200", CultureInfo.InvariantCulture);
        Assert.Equal(0.0, result);
    }
}

public class RiskLevelToColorConverterTests
{
    private readonly RiskLevelToColorConverter _converter = new();

    [Theory]
    [InlineData(RiskLevel.Safe)]
    [InlineData(RiskLevel.Low)]
    [InlineData(RiskLevel.Medium)]
    [InlineData(RiskLevel.High)]
    [InlineData(RiskLevel.Critical)]
    public void Convert_ValidRiskLevel_ReturnsSolidColorBrush(RiskLevel risk)
    {
        var result = _converter.Convert(risk, typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.IsType<SolidColorBrush>(result);
    }

    [Fact]
    public void Convert_Safe_ReturnsGreen()
    {
        var result = (SolidColorBrush)_converter.Convert(RiskLevel.Safe, typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(34, 197, 94), result.Color);
    }

    [Fact]
    public void Convert_Critical_ReturnsRed()
    {
        var result = (SolidColorBrush)_converter.Convert(RiskLevel.Critical, typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(220, 38, 38), result.Color);
    }

    [Fact]
    public void Convert_NonRiskLevel_ReturnsGrayBrush()
    {
        var result = (SolidColorBrush)_converter.Convert("invalid", typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(148, 163, 184), result.Color);
    }
}

public class MigrationStatusToColorConverterTests
{
    private readonly MigrationStatusToColorConverter _converter = new();

    [Fact]
    public void Convert_Completed_ReturnsGreen()
    {
        var result = (SolidColorBrush)_converter.Convert(MigrationStatus.Completed, typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(34, 197, 94), result.Color);
    }

    [Fact]
    public void Convert_Failed_ReturnsRed()
    {
        var result = (SolidColorBrush)_converter.Convert(MigrationStatus.Failed, typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(239, 68, 68), result.Color);
    }

    [Fact]
    public void Convert_RolledBack_ReturnsAmber()
    {
        var result = (SolidColorBrush)_converter.Convert(MigrationStatus.RolledBack, typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(245, 158, 11), result.Color);
    }

    [Fact]
    public void Convert_Moving_ReturnsBlue()
    {
        var result = (SolidColorBrush)_converter.Convert(MigrationStatus.Moving, typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(59, 130, 246), result.Color);
    }

    [Fact]
    public void Convert_Pending_ReturnsGray()
    {
        var result = (SolidColorBrush)_converter.Convert(MigrationStatus.Pending, typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(148, 163, 184), result.Color);
    }

    [Fact]
    public void Convert_NonStatus_ReturnsGray()
    {
        var result = (SolidColorBrush)_converter.Convert("invalid", typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(148, 163, 184), result.Color);
    }
}

public class RecommendationToColorConverterTests
{
    private readonly RecommendationToColorConverter _converter = new();

    [Fact]
    public void Convert_Recommended_ReturnsGreen()
    {
        var result = (SolidColorBrush)_converter.Convert(Recommendation.Recommended, typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(34, 197, 94), result.Color);
    }

    [Fact]
    public void Convert_Caution_ReturnsAmber()
    {
        var result = (SolidColorBrush)_converter.Convert(Recommendation.Caution, typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(245, 158, 11), result.Color);
    }

    [Fact]
    public void Convert_NotRecommended_ReturnsRed()
    {
        var result = (SolidColorBrush)_converter.Convert(Recommendation.NotRecommended, typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(239, 68, 68), result.Color);
    }

    [Fact]
    public void Convert_NonRecommendation_ReturnsGray()
    {
        var result = (SolidColorBrush)_converter.Convert("invalid", typeof(Brush), null!, CultureInfo.InvariantCulture);
        Assert.Equal(Color.FromRgb(148, 163, 184), result.Color);
    }
}

public class ReferenceTypeToIconConverterTests
{
    private readonly ReferenceTypeToIconConverter _converter = new();

    [Theory]
    [InlineData(ReferenceType.RegistryValue, "🔑")]
    [InlineData(ReferenceType.Shortcut, "🔗")]
    [InlineData(ReferenceType.WindowsService, "⚙")]
    [InlineData(ReferenceType.EnvironmentVariable, "📋")]
    [InlineData(ReferenceType.ScheduledTask, "⏰")]
    [InlineData(ReferenceType.ConfigFile, "📄")]
    public void Convert_ValidType_ReturnsExpectedIcon(ReferenceType type, string expectedIcon)
    {
        var result = _converter.Convert(type, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal(expectedIcon, result);
    }

    [Fact]
    public void Convert_NonReferenceType_ReturnsQuestionMark()
    {
        var result = _converter.Convert("invalid", typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal("❓", result);
    }
}
