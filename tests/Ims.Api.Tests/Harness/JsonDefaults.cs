using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ims.Api.Tests.Harness;

/// <summary>Serializer options that match the API's wire format (camelCase, enums as strings).</summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
