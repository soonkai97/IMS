using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
namespace Ims.Api;

internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo { Title = OpenApiConstants.Title, Version = OpenApiConstants.DocumentName };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            [OpenApiConstants.BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = OpenApiConstants.BearerScheme.ToLowerInvariant(),
                BearerFormat = OpenApiConstants.BearerFormat,
                Description = OpenApiConstants.BearerDescription
            }
        };
        document.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(OpenApiConstants.BearerScheme, document)] = [] }
        ];
        return Task.CompletedTask;
    }
}
