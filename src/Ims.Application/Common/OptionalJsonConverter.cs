using System.Text.Json;
using System.Text.Json.Serialization;
namespace Ims.Application;

/// <summary>
/// Reads and writes <see cref="Optional{T}"/> as the bare value. A key that is not in the JSON never reaches the converter, so the field stays <c>default</c> (absent); a key that is there,
/// including <c>null</c> (which a converter only sees with <c>HandleNull</c>), becomes a present value. A value of the wrong type is a <see cref="JsonException"/>, which the API answers with a 400.
/// </summary>
public sealed class OptionalJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class Converter<T> : JsonConverter<Optional<T>>
    {
        public override bool HandleNull => true;

        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? new Optional<T>(default) : new Optional<T>(JsonSerializer.Deserialize<T>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            if (value.Value is null) writer.WriteNullValue();
            else JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
