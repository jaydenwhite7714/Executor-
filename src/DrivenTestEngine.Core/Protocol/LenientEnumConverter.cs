using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DrivenTestEngine.Core.Protocol;

/// <summary>
/// Enum converter that maps enum members to lower-case wire values and never throws on an
/// unrecognised value coming from a newer server: it falls back to the enum's default member.
/// </summary>
public sealed class LenientEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var text = reader.GetString();
                return Parse(text);
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var raw) && Enum.IsDefined(typeof(TEnum), raw))
                {
                    return (TEnum)Enum.ToObject(typeof(TEnum), raw);
                }

                return default;
            case JsonTokenType.Null:
                return default;
            default:
                reader.Skip();
                return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(ToWireValue(value));
    }

    public static TEnum Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        var normalized = text.Trim().Replace("-", string.Empty).Replace("_", string.Empty);
        if (Enum.TryParse<TEnum>(normalized, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return default;
    }

    public static string ToWireValue(TEnum value) =>
        value.ToString().ToLower(CultureInfo.InvariantCulture);
}

/// <summary>Applies the lenient enum converter to every enum it encounters.</summary>
public sealed class LenientEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(LenientEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}
