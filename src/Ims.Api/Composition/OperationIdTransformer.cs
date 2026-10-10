using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
namespace Ims.Api;

/// <summary>
/// Stable operation ids of the form <c>Tag_action</c> (controller and action name, action in camelCase), for example <c>Documents_createReceipt</c>.
/// A generated client names its classes after the tag and its methods after the action, so renaming a route does not rename the client method.
/// </summary>
internal sealed class OperationIdTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor is ControllerActionDescriptor action)
            operation.OperationId = OperationId(action.ControllerName, action.ActionName);
        return Task.CompletedTask;
    }

    internal static string OperationId(string controller, string action) =>
        string.Concat(controller, OpenApiConstants.OperationIdSeparator, char.ToLowerInvariant(action[0]).ToString(), action[1..]);
}
