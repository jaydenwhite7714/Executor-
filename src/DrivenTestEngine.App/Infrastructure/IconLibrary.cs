using System.Windows;
using System.Windows.Media;

namespace DrivenTestEngine.App.Infrastructure;

/// <summary>Resolves vector icon geometries from the merged theme dictionary.</summary>
public static class IconLibrary
{
    public const string Fallback = "Icon.Tests";

    public static Geometry? Get(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Resource(Fallback);
        }

        var resourceKey = key.StartsWith("Icon.", StringComparison.Ordinal) ? key : "Icon." + key;
        return Resource(resourceKey) ?? Resource(Fallback);
    }

    private static Geometry? Resource(string key)
    {
        var app = Application.Current;
        var value = app?.TryFindResource(key);
        return value as Geometry;
    }
}
