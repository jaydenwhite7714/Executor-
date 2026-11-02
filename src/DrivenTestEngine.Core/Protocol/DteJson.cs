using System.Text.Json;
using System.Text.Json.Serialization;

namespace DrivenTestEngine.Core.Protocol;

/// <summary>Shared, immutable JSON settings for every payload on the wire.</summary>
public static class DteJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        options.Converters.Add(new LenientEnumConverterFactory());
        return options;
    }
}
