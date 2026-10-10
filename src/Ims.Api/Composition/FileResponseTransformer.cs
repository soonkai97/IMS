using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
namespace Ims.Api;

/// <summary>Declares the binary body (and the Content-Disposition header that names the file) for actions marked <see cref="ProducesFileAttribute"/>.</summary>
internal sealed class FileResponseTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor is not ControllerActionDescriptor action) return Task.CompletedTask;
        var file = action.MethodInfo.GetCustomAttributes(typeof(ProducesFileAttribute), inherit: false).OfType<ProducesFileAttribute>().FirstOrDefault();
        if (file is null || operation.Responses is null) return Task.CompletedTask;

        var response = new OpenApiResponse
        {
            Description = OpenApiConstants.FileResponseDescription,
            Content = file.ContentTypes.ToDictionary(
                contentType => contentType,
                _ => new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = OpenApiConstants.BinaryFormat } }),
            Headers = new Dictionary<string, IOpenApiHeader>
            {
                [HeaderNames.ContentDisposition] = new OpenApiHeader
                {
                    Description = OpenApiConstants.ContentDispositionDescription,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                }
            }
        };
        operation.Responses[StatusCodes.Status200OK.ToString()] = response;
        return Task.CompletedTask;
    }
}
