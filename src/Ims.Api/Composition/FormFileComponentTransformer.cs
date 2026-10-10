using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
namespace Ims.Api;

/// <summary>Drops the empty <c>IFormFile</c> component: uploads are described inline as binary strings (see <see cref="MultipartRequiredTransformer"/>).</summary>
internal sealed class FormFileComponentTransformer : IOpenApiDocumentTransformer
{
    private const string FormFileSchema = "IFormFile";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components?.Schemas?.Remove(FormFileSchema);
        return Task.CompletedTask;
    }
}
