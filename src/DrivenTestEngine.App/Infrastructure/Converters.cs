using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DrivenTestEngine.Core.Engine;
using DrivenTestEngine.Core.Models;

namespace DrivenTestEngine.App.Infrastructure;

internal static class ThemeBrush
{
    public static Brush Get(string key, string fallback = "Brush.TextSecondary")
    {
        var value = Application.Current?.TryFindResource(key) as Brush;
        if (value is not null)
        {
            return value;
        }

        var fallbackBrush = Application.Current?.TryFindResource(fallback) as Brush;
        return fallbackBrush ?? Brushes.Gray;
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Invert)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible ^ Invert;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Collapses when the bound string is null/whitespace.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasText = !string.IsNullOrWhiteSpace(value as string);
        if (Invert)
        {
            hasText = !hasText;
        }

        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible only when the bound integer is zero (empty states).</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        return count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible only when the bound value is not null.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DetectionLevelBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DetectionLevel.Critical => ThemeBrush.Get("Brush.Danger"),
        DetectionLevel.Error => ThemeBrush.Get("Brush.Danger"),
        DetectionLevel.Warning => ThemeBrush.Get("Brush.Warning"),
        DetectionLevel.Info => ThemeBrush.Get("Brush.Info"),
        DetectionLevel.Trace => ThemeBrush.Get("Brush.TextMuted"),
        _ => ThemeBrush.Get("Brush.TextMuted"),
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RunStateBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        TestRunState.Passed => ThemeBrush.Get("Brush.Success"),
        TestRunState.Failed => ThemeBrush.Get("Brush.Warning"),
        TestRunState.Errored => ThemeBrush.Get("Brush.Danger"),
        TestRunState.Rejected => ThemeBrush.Get("Brush.Danger"),
        TestRunState.Cancelled => ThemeBrush.Get("Brush.TextMuted"),
        TestRunState.Expired => ThemeBrush.Get("Brush.TextMuted"),
        TestRunState.Running => ThemeBrush.Get("Brush.AccentBright"),
        TestRunState.Queued => ThemeBrush.Get("Brush.Info"),
        _ => ThemeBrush.Get("Brush.TextMuted"),
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RunStateTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TestRunState state ? state.ToDisplayText() : "—";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ConnectionStateBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ConnectionState.Connected => ThemeBrush.Get("Brush.Success"),
        ConnectionState.Connecting => ThemeBrush.Get("Brush.Warning"),
        ConnectionState.Disconnecting => ThemeBrush.Get("Brush.Warning"),
        ConnectionState.Faulted => ThemeBrush.Get("Brush.Danger"),
        _ => ThemeBrush.Get("Brush.TextMuted"),
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        AntiCheatSeverity.Critical => ThemeBrush.Get("Brush.Danger"),
        AntiCheatSeverity.High => ThemeBrush.Get("Brush.Danger"),
        AntiCheatSeverity.Medium => ThemeBrush.Get("Brush.Warning"),
        AntiCheatSeverity.Low => ThemeBrush.Get("Brush.Info"),
        AntiCheatSeverity.Info => ThemeBrush.Get("Brush.TextSecondary"),
        _ => ThemeBrush.Get("Brush.TextMuted"),
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class IntegrityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? ThemeBrush.Get("Brush.Success") : ThemeBrush.Get("Brush.Danger");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Picks the tick or cross geometry for an integrity check.</summary>
public sealed class IntegrityIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        IconLibrary.Get(value is true ? "Icon.Check" : "Icon.Alert");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formats a nullable double for compact rows.</summary>
public sealed class NullableNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double number ? number.ToString("0.##", CultureInfo.CurrentCulture) : "—";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
