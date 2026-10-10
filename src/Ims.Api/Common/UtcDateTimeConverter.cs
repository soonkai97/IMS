using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Ims.Api;

/// <summary>
/// Every instant on the wire is UTC with an explicit <c>Z</c>. The database stores UTC and EF materialises it as
/// <see cref="DateTimeKind.Unspecified"/>, which the default serializer would write without a zone designator (the client then reads it as local time).
/// Unspecified is treated as UTC; a local value is converted.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ToUtc(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToUtc(value).ToString(Format, CultureInfo.InvariantCulture));

    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
