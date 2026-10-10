using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
namespace Ims.Api;

/// <summary>
/// Every enum travels as its name (see <see cref="WireJson"/>), so every enum schema is a plain string schema listing exactly those names.
/// The generator leaves out <c>type</c> and, once an enum has been used as nullable somewhere, adds <c>null</c> to the shared list of values;
/// a generated client would turn either into a worse type, so both are repaired here (nullability belongs to the property, not to the enum).
/// </summary>
internal sealed class EnumSchemaTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var schema in document.Components?.Schemas?.Values.OfType<OpenApiSchema>() ?? [])
        {
            if (schema.Enum is not { Count: > 0 } values) continue;
            for (var i = values.Count - 1; i >= 0; i--)
                if (values[i] is null || values[i]!.GetValueKind() == JsonValueKind.Null) values.RemoveAt(i);
            schema.Type = JsonSchemaType.String;
        }
        return Task.CompletedTask;
    }
}
