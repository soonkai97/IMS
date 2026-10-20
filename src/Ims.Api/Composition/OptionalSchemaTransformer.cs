using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
namespace Ims.Api;

/// <summary>
/// <c>Optional&lt;string&gt;</c> travels as the bare value (see <c>OptionalJsonConverter</c>), so a property of that type is a nullable string that may be left out. The generator cannot read a custom converter
/// and publishes a shared object schema (<c>OptionalOfstring</c>) that a client would turn into a wrapper type; each property is put back to a plain nullable string (keeping its own description) and the
/// shared schema is dropped. Absent is not in the schema: the property is simply not required. Another value type needs its own mapping here, and so does an <c>Optional</c> anywhere but a property of a named
/// schema (an array item, a dictionary value, an inline schema): both fail the document test instead of leaking a wrapper type into the client.
/// </summary>
public sealed class OptionalSchemaTransformer : IOpenApiDocumentTransformer
{
    private const string Prefix = "OptionalOf";

    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var schemas = document.Components?.Schemas;
        if (schemas is null) return;

        foreach (var owner in schemas.Values.OfType<OpenApiSchema>())
            foreach (var (name, property) in (owner.Properties ?? new Dictionary<string, IOpenApiSchema>()).ToList())
                if (property is OpenApiSchemaReference { Reference.Id: { } id } reference && id.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    if (id != Prefix + "string") throw new NotSupportedException($"{owner.Title ?? "A schema"}.{name} is {id}, which has no OpenAPI mapping yet: add it to {nameof(OptionalSchemaTransformer)}.");
                    owner.Properties![name] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, Description = reference.Description };
                }

        foreach (var id in schemas.Keys.Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)).ToList()) schemas.Remove(id);

        // Whatever still points at a removed schema was not a plain property: say so now, not as a broken reference in a generated client.
        var json = await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, cancellationToken);
        if (json.Contains("#/components/schemas/" + Prefix, StringComparison.Ordinal))
            throw new NotSupportedException($"An Optional<T> is used somewhere other than a property of a named schema (an array item, a dictionary value or an inline schema): map it in {nameof(OptionalSchemaTransformer)}.");
    }
}
