using Ims.Api;
using Microsoft.OpenApi;

namespace Ims.Api.Tests.Tests.Contract;

/// <summary>The rewrite of <c>Optional&lt;string&gt;</c> in the OpenAPI document, and its refusal of every other place an Optional could turn up (no leaked wrapper schema in the generated client).</summary>
public class OptionalSchemaTransformerTests
{
    private static OpenApiDocument Document(Func<OpenApiDocument, IOpenApiSchema> owner)
    {
        var document = new OpenApiDocument { Components = new OpenApiComponents { Schemas = new Dictionary<string, IOpenApiSchema>() } };
        document.Components.Schemas!["OptionalOfstring"] = new OpenApiSchema { Type = JsonSchemaType.Object };
        document.Components.Schemas["Request"] = owner(document);
        return document;
    }

    private static Task RunAsync(OpenApiDocument document) => new OptionalSchemaTransformer().TransformAsync(document, null!, CancellationToken.None);

    private static OpenApiSchemaReference Optional(OpenApiDocument document, string? description = null) => new("OptionalOfstring", document) { Description = description };

    [Fact]
    public async Task A_property_becomes_a_nullable_string_with_its_own_description_and_the_shared_schema_goes()
    {
        var document = Document(d => new OpenApiSchema { Type = JsonSchemaType.Object, Properties = new Dictionary<string, IOpenApiSchema> { ["hsCode"] = Optional(d, "HS code") } });

        await RunAsync(document);

        var property = Assert.IsType<OpenApiSchema>(((OpenApiSchema)document.Components!.Schemas!["Request"]).Properties!["hsCode"]);
        Assert.Equal(JsonSchemaType.String | JsonSchemaType.Null, property.Type);
        Assert.Equal("HS code", property.Description);
        Assert.False(document.Components.Schemas.ContainsKey("OptionalOfstring"));
    }

    [Fact]
    public async Task An_Optional_of_another_type_is_refused()
    {
        var document = Document(d => new OpenApiSchema { Type = JsonSchemaType.Object, Properties = new Dictionary<string, IOpenApiSchema> { ["n"] = new OpenApiSchemaReference("OptionalOfint32", d) } });
        document.Components!.Schemas!["OptionalOfint32"] = new OpenApiSchema { Type = JsonSchemaType.Object };

        await Assert.ThrowsAsync<NotSupportedException>(() => RunAsync(document));
    }

    [Fact]
    public async Task An_Optional_as_an_array_item_or_a_dictionary_value_is_refused_instead_of_leaking_a_wrapper_schema()
    {
        var inArray = Document(d => new OpenApiSchema { Type = JsonSchemaType.Object, Properties = new Dictionary<string, IOpenApiSchema> { ["list"] = new OpenApiSchema { Type = JsonSchemaType.Array, Items = Optional(d) } } });
        var inDictionary = Document(d => new OpenApiSchema { Type = JsonSchemaType.Object, Properties = new Dictionary<string, IOpenApiSchema> { ["map"] = new OpenApiSchema { Type = JsonSchemaType.Object, AdditionalProperties = Optional(d) } } });

        await Assert.ThrowsAsync<NotSupportedException>(() => RunAsync(inArray));
        await Assert.ThrowsAsync<NotSupportedException>(() => RunAsync(inDictionary));
    }
}
