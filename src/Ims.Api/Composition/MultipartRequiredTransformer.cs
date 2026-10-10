using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
namespace Ims.Api;

/// <summary>
/// Describes the file parts of a multipart body: each is an inline binary string (not a reference to a shared, empty <c>IFormFile</c> component)
/// and required, because an upload without its file is a 400 and a generated client should type the file as non-optional.
/// </summary>
internal sealed class MultipartRequiredTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (operation.RequestBody?.Content is null || !operation.RequestBody.Content.TryGetValue(OpenApiConstants.MultipartFormData, out var media))
            return Task.CompletedTask;
        if (media.Schema is not OpenApiSchema { Properties: { } properties } schema) return Task.CompletedTask;

        schema.Required ??= new HashSet<string>();
        foreach (var name in properties.Where(p => p.Value.Format == OpenApiConstants.BinaryFormat).Select(p => p.Key).ToList())
        {
            properties[name] = new OpenApiSchema { Type = JsonSchemaType.String, Format = OpenApiConstants.BinaryFormat };
            schema.Required.Add(name);
        }
        return Task.CompletedTask;
    }
}
