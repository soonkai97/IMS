using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
namespace Ims.Api;

/// <summary>
/// <c>Optional&lt;string&gt;</c> travels as the bare value (see <c>OptionalJsonConverter</c>), so a property of that type is a nullable string that may be left out. The generator cannot read a custom converter
/// and publishes a shared object schema (<c>OptionalOfstring</c>) that a client would turn into a wrapper type; each property is put back to a plain nullable string (keeping its own description) and the
/// shared schema is dropped. Absent is not in the schema: the property is simply not required. Another value type needs its own mapping here, so a new one fails the document test instead of leaking out.
/// </summary>
internal sealed class OptionalSchemaTransformer : IOpenApiDocumentTransformer
{
    private const string Prefix = "OptionalOf";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var schemas = document.Components?.Schemas;
        if (schemas is null) return Task.CompletedTask;

        foreach (var owner in schemas.Values.OfType<OpenApiSchema>())
            foreach (var (name, property) in (owner.Properties ?? new Dictionary<string, IOpenApiSchema>()).ToList())
                if (property is OpenApiSchemaReference { Reference.Id: { } id } reference && id.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    if (id != Prefix + "string") throw new NotSupportedException($"{owner.Title ?? "A schema"}.{name} is {id}, which has no OpenAPI mapping yet: add it to {nameof(OptionalSchemaTransformer)}.");
                    owner.Properties![name] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, Description = reference.Description };
                }

        foreach (var id in schemas.Keys.Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)).ToList()) schemas.Remove(id);
        return Task.CompletedTask;
    }
}
