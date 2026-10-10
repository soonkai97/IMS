using System.Text.Json;
using System.Text.Json.Serialization;
namespace Ims.Api;

/// <summary>
/// One definition of the JSON wire format. MVC is what is actually sent; the minimal-API options are only read by the OpenAPI
/// generator, so they get the parts that change the published schema: enums are strings with their values listed and numbers are plain
/// numbers. Instants are UTC with a <c>Z</c> (a converter the generator would not know how to describe, so MVC only).
/// </summary>
public static class WireJson
{
    public static IServiceCollection AddWireJson(this IServiceCollection services)
    {
        services.AddControllers().AddJsonOptions(o => Configure(o.JsonSerializerOptions));
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            // Numbers are published as plain OpenAPI numbers, not as ["integer","string"] with a digits pattern.
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        return services;
    }

    public static void Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new UtcDateTimeConverter());
        options.Converters.Add(new UtcNullableDateTimeConverter());
    }
}
