using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Ims.Api;

/// <summary>Nullable counterpart of <see cref="UtcDateTimeConverter"/>.</summary>
public sealed class UtcNullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : UtcDateTimeConverter.ToUtc(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is { } v) writer.WriteStringValue(UtcDateTimeConverter.ToUtc(v).ToString(UtcDateTimeConverter.Format, CultureInfo.InvariantCulture));
        else writer.WriteNullValue();
    }
}
