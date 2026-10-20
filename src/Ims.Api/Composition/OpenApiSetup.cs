namespace Ims.Api;

public static class OpenApiSetup
{
    /// <summary>OpenAPI document (also the source for the NSwag-generated TypeScript client) with a Bearer-token scheme.</summary>
    public static IServiceCollection AddOpenApiDocument(this IServiceCollection services)
    {
        services.AddOpenApi(OpenApiConstants.DocumentName, o =>
        {
            o.AddDocumentTransformer<BearerSecurityTransformer>();
            o.AddOperationTransformer<OperationIdTransformer>();
            o.AddOperationTransformer<FileResponseTransformer>();
            o.AddOperationTransformer<MultipartRequiredTransformer>();
            o.AddDocumentTransformer<FormFileComponentTransformer>();
            o.AddDocumentTransformer<EnumSchemaTransformer>();
            o.AddDocumentTransformer<OptionalSchemaTransformer>();
        });
        return services;
    }

    /// <summary>Swagger UI over the generated document. Not exposed in production.</summary>
    public static WebApplication UseSwaggerUi(this WebApplication app)
    {
        app.UseSwaggerUI(o =>
        {
            o.RoutePrefix = OpenApiConstants.UiRoute;
            o.SwaggerEndpoint(OpenApiConstants.DocumentPath, OpenApiConstants.Title);
            o.DocumentTitle = OpenApiConstants.Title;
            o.EnablePersistAuthorization();
        });
        return app;
    }
}
