using System.Text.Json;
using System.Text.RegularExpressions;
using Ims.Api.Tests.Harness;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Contract;

/// <summary>
/// The published OpenAPI document is the contract a client is generated from. It must describe the wire format exactly as it is, so no client
/// needs a workaround: string enums with their values, stable operation ids, binary uploads and downloads, plain numbers.
/// </summary>
[Collection(ApiCollection.Name)]
public partial class OpenApiDocumentTests(ApiFixture fixture)
{
    private static readonly string[] Verbs = ["get", "post", "put", "delete", "patch"];

    [GeneratedRegex(@"^[A-Z][A-Za-z]+_[a-z][A-Za-z0-9]*$")]
    private static partial Regex OperationIdShape();

    private async Task<JsonDocument> DocumentAsync() => (await fixture.Anonymous().GetAsync("/openapi/v1.json")).ShouldBeOk().Json();

    private static IEnumerable<(string Path, string Verb, JsonElement Operation)> Operations(JsonElement document)
    {
        foreach (var path in document.GetProperty("paths").EnumerateObject())
            foreach (var verb in path.Value.EnumerateObject().Where(v => Verbs.Contains(v.Name)))
                yield return (path.Name, verb.Name, verb.Value);
    }

    private static IEnumerable<JsonElement> Everything(JsonElement element)
    {
        yield return element;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var child in element.EnumerateObject().SelectMany(p => Everything(p.Value))) yield return child;
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray().SelectMany(Everything)) yield return child;
                break;
        }
    }

    // ----- enums -----
    [Fact]
    public async Task Every_enum_is_a_string_schema_listing_its_values_exactly_as_they_are_sent()
    {
        using var doc = await DocumentAsync();
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        var enums = typeof(DocumentStatus).Assembly.GetTypes().Concat(typeof(Ims.Application.IDispatcher).Assembly.GetTypes())
            .Where(t => t.IsEnum && schemas.TryGetProperty(t.Name, out _))
            .ToList();

        // The ones the client needs, so the test cannot pass by finding nothing.
        string[] required = ["DocumentStatus", "DocumentType", "IssuePurpose", "LedgerReason", "PartnerType", "ProductSort", "StockSort", "ImportRowStatus"];
        Assert.All(required, name => Assert.Contains(enums, t => t.Name == name));

        foreach (var type in enums)
        {
            var schema = schemas.GetProperty(type.Name);
            Assert.True(schema.TryGetProperty("enum", out var values), $"{type.Name} lists no values.");
            Assert.Equal(Enum.GetNames(type), values.EnumerateArray().Select(v => v.GetString())); // no null in the list: nullability belongs to the property
            Assert.Equal("string", schema.GetProperty("type").GetString());
        }
    }

    [Fact]
    public async Task No_schema_describes_an_enum_as_a_bare_integer()
    {
        using var doc = await DocumentAsync();

        foreach (var name in new[] { "DocumentStatus", "DocumentType", "LedgerReason", "PartnerType", "ProductSort", "StockSort" })
        {
            var schema = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name);
            Assert.False(schema.TryGetProperty("type", out var kind) && kind.ToString().Contains("integer"), $"{name} is published as an integer.");
        }
    }

    // ----- numbers -----
    [Fact]
    public async Task Numbers_are_plain_numbers_not_arrays_of_number_and_string()
    {
        using var doc = await DocumentAsync();

        var offenders = Everything(doc.RootElement)
            .Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.Array)
            .Where(e => e.GetProperty("type").EnumerateArray().Any(t => t.GetString() is "integer" or "number")
                        && e.GetProperty("type").EnumerateArray().Any(t => t.GetString() == "string"))
            .Select(e => e.GetRawText()).Take(3).ToList();

        Assert.True(offenders.Count == 0, "Numeric schemas that also allow strings: " + string.Join(" | ", offenders));
        Assert.DoesNotContain(Everything(doc.RootElement), e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("pattern", out var p) && p.GetString()!.Contains(@"\d"));
    }

    // ----- public ids (GUID plan: no integer id on the wire) -----

    /// <summary>
    /// The only properties and parameters named like an id that are NOT a public id: the position of a ledger row and of an audit row (neither is addressable), and the audit
    /// entity id, which is text (a public id, a document number or an old integer). Everything else that is called <c>id</c> or ends in <c>Id</c> or <c>Ids</c> is a GUID.
    /// </summary>
    private static readonly string[] PositionIds = ["LedgerRow.id", "AuditLogDto.id", "AuditLogDto.entityId", "get /api/audit entityId"];

    /// <summary>Query parameters of a bound filter record keep the C# casing (<c>WarehouseId</c>), properties and route values are camel case: positions are compared without case.</summary>
    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsIdName(string name) => name.Equals("id", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Id", StringComparison.Ordinal) || name.EndsWith("Ids", StringComparison.Ordinal);

    /// <summary>The schema is a string with format uuid, possibly nullable (a type list, or a oneOf/anyOf with a null) or the item of a list of them.</summary>
    private static bool IsUuid(JsonElement schema)
    {
        if (schema.TryGetProperty("items", out var items)) return IsUuid(items);
        foreach (var key in new[] { "oneOf", "anyOf" })
            if (schema.TryGetProperty(key, out var options)) return options.EnumerateArray().Where(o => !o.TryGetProperty("type", out var t) || t.ToString() != "null").All(IsUuid);
        return schema.TryGetProperty("format", out var format) && format.GetString() == "uuid"
               && (!schema.TryGetProperty("type", out var type) || !type.ToString().Contains("integer"));
    }

    /// <summary>Every property of every schema in the document, named or inline (the multipart form of an upload is inline), with where it was found.</summary>
    private static IEnumerable<(string Where, string Name, JsonElement Schema)> Properties(JsonElement root)
    {
        foreach (var schema in root.GetProperty("components").GetProperty("schemas").EnumerateObject())
            if (schema.Value.TryGetProperty("properties", out var named))
                foreach (var property in named.EnumerateObject()) yield return ($"{schema.Name}.{property.Name}", property.Name, property.Value);

        foreach (var (path, verb, operation) in Operations(root))
            if (operation.TryGetProperty("requestBody", out var body) && body.GetProperty("content").TryGetProperty("multipart/form-data", out var form)
                && form.GetProperty("schema").TryGetProperty("properties", out var fields))
                foreach (var field in fields.EnumerateObject()) yield return ($"{verb} {path} form {field.Name}", field.Name, field.Value);
    }

    private static IEnumerable<(string Where, string Name, JsonElement Schema)> Parameters(JsonElement root) =>
        Operations(root).SelectMany(o => o.Operation.TryGetProperty("parameters", out var p)
            ? p.EnumerateArray().Select(x => ($"{o.Verb} {o.Path} {x.GetProperty("name").GetString()}", x.GetProperty("name").GetString()!, x.GetProperty("schema")))
            : []);

    [Fact]
    public async Task Every_id_on_the_wire_is_a_uuid_except_the_listed_positions()
    {
        using var doc = await DocumentAsync();
        var root = doc.RootElement;

        var properties = Properties(root).Where(x => IsIdName(x.Name)).ToList();
        var parameters = Parameters(root).Where(x => IsIdName(x.Name)).ToList();

        // The test cannot pass by finding nothing: the ids of every entity a client addresses are in the set it checks.
        string[] expected = ["WarehouseDto.id", "PartnerDto.id", "CategoryDto.id", "UomDto.id", "UserDto.id", "UserInfo.id", "ProductDto.id", "ApprovalRuleDto.id", "DocumentDto.id",
            "DocumentListItem.id", "AttachmentDto.id", "AttachmentDto.ownerId", "LedgerRow.sourceDocId", "ImportResult.documentId", "DocumentLineDto.productId", "StockRow.productId"];
        Assert.All(expected, name => Assert.Contains(properties, x => Same(x.Where, name)));
        Assert.Contains(properties, x => Same(x.Where, "post /api/attachments form ownerId"));
        Assert.Contains(parameters, x => Same(x.Where, "get /api/documents/{id} id"));
        Assert.Contains(parameters, x => Same(x.Where, "get /api/attachments ownerId"));
        Assert.Contains(parameters, x => Same(x.Where, "get /api/stock/ledger sourceDocId"));
        Assert.Contains(parameters, x => Same(x.Where, "put /api/approval-rules/{id} id"));
        Assert.True(properties.Count >= 40 && parameters.Count >= 30, $"{properties.Count} properties and {parameters.Count} parameters named like an id");

        // The allow-list holds only positions that exist (a rename must not leave a stale entry that hides a new integer id).
        Assert.All(PositionIds, name => Assert.True(properties.Concat(parameters).Any(x => Same(x.Where, name)), $"{name} is not in the document any more"));

        var notUuid = properties.Concat(parameters).Where(x => !PositionIds.Any(p => Same(x.Where, p)) && !IsUuid(x.Schema)).Select(x => x.Where).ToList();
        Assert.True(notUuid.Count == 0, "Ids that are not a uuid: " + string.Join(", ", notUuid));
    }

    [Fact]
    public async Task A_document_line_has_no_id_of_its_own()
    {
        using var doc = await DocumentAsync();

        var line = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("DocumentLineDto").GetProperty("properties");

        Assert.False(line.TryGetProperty("id", out _));
        Assert.True(line.TryGetProperty("productId", out _));
    }

    // ----- operation ids -----
    [Fact]
    public async Task Every_operation_has_a_unique_id_of_the_form_Tag_action()
    {
        using var doc = await DocumentAsync();

        var all = Operations(doc.RootElement).ToList();
        Assert.True(all.Count > 40);
        foreach (var (path, verb, operation) in all)
        {
            Assert.True(operation.TryGetProperty("operationId", out var id), $"{verb.ToUpperInvariant()} {path} has no operationId.");
            var text = id.GetString()!;
            Assert.Matches(OperationIdShape(), text);
            Assert.Equal(operation.GetProperty("tags")[0].GetString(), text.Split('_')[0]);
        }
        var duplicates = all.GroupBy(o => o.Operation.GetProperty("operationId").GetString()).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, "Duplicate operation ids: " + string.Join(", ", duplicates));
    }

    [Theory]
    [InlineData("/api/documents", "get", "Documents_list")]
    [InlineData("/api/documents/{id}", "get", "Documents_get")]
    [InlineData("/api/documents/receipts", "post", "Documents_createReceipt")]
    [InlineData("/api/documents/counts", "get", "Documents_counts")]
    [InlineData("/api/products/lookup", "get", "Products_lookup")]
    [InlineData("/api/import/products", "post", "Import_importProducts")]
    [InlineData("/api/import/templates/{kind}", "get", "Import_template")]
    [InlineData("/api/reports/movements/export", "get", "Reports_exportMovements")]
    [InlineData("/api/attachments/{id}", "get", "Attachments_download")]
    [InlineData("/api/attachments", "post", "Attachments_upload")]
    public async Task Operation_ids_the_client_depends_on_are_pinned(string path, string verb, string expected)
    {
        using var doc = await DocumentAsync();

        Assert.Equal(expected, doc.RootElement.GetProperty("paths").GetProperty(path).GetProperty(verb).GetProperty("operationId").GetString());
    }

    // ----- uploads -----
    [Fact]
    public async Task Uploads_are_multipart_with_a_required_inline_binary_file_part()
    {
        using var doc = await DocumentAsync();

        var uploads = Operations(doc.RootElement).Where(o => o.Operation.TryGetProperty("requestBody", out var b) && b.GetProperty("content").TryGetProperty("multipart/form-data", out _)).ToList();
        Assert.True(uploads.Count >= 3, "import products, import opening stock and attachments are expected");

        foreach (var (path, verb, operation) in uploads)
        {
            var schema = operation.GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data").GetProperty("schema");
            var file = schema.GetProperty("properties").GetProperty("file");
            Assert.False(file.TryGetProperty("$ref", out _), $"{verb.ToUpperInvariant()} {path}: the file part is a reference, not an inline binary string.");
            Assert.Equal("string", file.GetProperty("type").GetString());
            Assert.Equal("binary", file.GetProperty("format").GetString());
            Assert.Contains("file", schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        }
        Assert.False(doc.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty("IFormFile", out _));
    }

    // ----- downloads -----
    [Theory]
    [InlineData("/api/reports/stock-summary/export")]
    [InlineData("/api/reports/movements/export")]
    [InlineData("/api/reports/ledger/export")]
    [InlineData("/api/reports/low-stock/export")]
    [InlineData("/api/import/templates/{kind}")]
    [InlineData("/api/attachments/{id}")]
    public async Task File_downloads_declare_a_binary_response_and_the_file_name_header(string path)
    {
        using var doc = await DocumentAsync();

        var ok = doc.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get").GetProperty("responses").GetProperty("200");
        var content = ok.GetProperty("content").EnumerateObject().ToList();
        Assert.NotEmpty(content);
        Assert.All(content, c =>
        {
            Assert.Equal("string", c.Value.GetProperty("schema").GetProperty("type").GetString());
            Assert.Equal("binary", c.Value.GetProperty("schema").GetProperty("format").GetString());
        });
        Assert.True(ok.GetProperty("headers").TryGetProperty("Content-Disposition", out _));
    }

    [Fact]
    public async Task Spreadsheet_downloads_name_the_xlsx_media_type()
    {
        using var doc = await DocumentAsync();

        var content = doc.RootElement.GetProperty("paths").GetProperty("/api/reports/movements/export").GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content");

        Assert.True(content.TryGetProperty("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", out _));
    }

    // ----- the override reason is given when approving, not when posting -----
    [Fact]
    public async Task Approve_takes_the_override_reason_and_post_takes_no_body()
    {
        using var doc = await DocumentAsync();
        var paths = doc.RootElement.GetProperty("paths");
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        var approve = paths.GetProperty("/api/documents/{id}/approve").GetProperty("post");
        var post = paths.GetProperty("/api/documents/{id}/post").GetProperty("post");

        Assert.True(approve.TryGetProperty("requestBody", out var body), "approve describes its optional body");
        Assert.False(body.TryGetProperty("required", out var required) && required.GetBoolean(), "the approve body is optional");
        Assert.True(schemas.GetProperty("ApproveRequest").GetProperty("properties").TryGetProperty("negativeStockOverrideReason", out _));
        Assert.False(post.TryGetProperty("requestBody", out _), "post takes no body");
        Assert.False(schemas.TryGetProperty("PostRequest", out _), "PostRequest is gone");
    }

    // ----- the contract changes of this release -----
    [Fact]
    public async Task The_new_document_fields_filters_and_endpoint_are_published()
    {
        using var doc = await DocumentAsync();
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        string[] documentFields = ["requiresApproval", "requiresOverride", "isLarge", "isReturned", "createdById", "createdByName", "submittedByName",
            "returnedById", "returnedBy", "returnedByName", "returnedAt", "concurrencyToken"];
        Assert.All(documentFields, f => Assert.True(schemas.GetProperty("DocumentDto").GetProperty("properties").TryGetProperty(f, out _), $"DocumentDto.{f}"));
        Assert.False(schemas.GetProperty("DocumentDto").GetProperty("properties").TryGetProperty("rowVersion", out _), "the raw row version is not part of the contract");
        foreach (var schemaName in new[] { "DocumentDto", "DocumentListItem" })
        {
            var schema = schemas.GetProperty(schemaName);
            Assert.Equal("string", schema.GetProperty("properties").GetProperty("concurrencyToken").GetProperty("type").GetString()); // never null
            Assert.Contains("concurrencyToken", schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        }

        string[] listFields = ["requiresApproval", "requiresOverride", "isLarge", "isReturned", "createdById", "createdByName", "submittedById", "submittedBy", "submittedByName", "submittedAt", "concurrencyToken"];
        Assert.All(listFields, f => Assert.True(schemas.GetProperty("DocumentListItem").GetProperty("properties").TryGetProperty(f, out _), $"DocumentListItem.{f}"));

        Assert.True(schemas.GetProperty("DocumentLineDto").GetProperty("properties").TryGetProperty("available", out _));
        Assert.True(schemas.GetProperty("StockRow").GetProperty("properties").TryGetProperty("warehouseName", out _));
        Assert.True(schemas.GetProperty("ImportRowResult").GetProperty("properties").TryGetProperty("status", out _));
        Assert.True(schemas.TryGetProperty("ProductLookupResult", out _));
        Assert.True(schemas.TryGetProperty("DocumentCounts", out _));

        var parameters = doc.RootElement.GetProperty("paths").GetProperty("/api/documents").GetProperty("get").GetProperty("parameters")
            .EnumerateArray().Select(p => p.GetProperty("name").GetString()!.ToLowerInvariant()).ToList();
        Assert.Contains("mine", parameters);
        Assert.Contains("requiresapproval", parameters);
    }

    [Fact]
    public async Task The_qa_fix_fields_are_published_and_never_null()
    {
        using var doc = await DocumentAsync();
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(schemas.GetProperty("DocumentDto").GetProperty("properties").TryGetProperty("approvalTriggers", out _));
        foreach (var (schema, field) in new[] { ("DocumentDto", "amountIsEstimate"), ("DocumentListItem", "amountIsEstimate"), ("DocumentListItem", "canAct") })
        {
            var property = schemas.GetProperty(schema).GetProperty("properties").GetProperty(field);
            Assert.Equal("boolean", property.GetProperty("type").GetString());
            Assert.Contains(field, schemas.GetProperty(schema).GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        }

        var metric = schemas.GetProperty("ApprovalMetric");
        Assert.Equal("string", metric.GetProperty("type").GetString());
        Assert.Equal(["Amount", "Quantity"], metric.GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task Timestamps_are_published_as_date_time_strings()
    {
        using var doc = await DocumentAsync();

        var createdAt = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("DocumentDto").GetProperty("properties").GetProperty("createdAt");

        Assert.Equal("string", createdAt.GetProperty("type").GetString());
        Assert.Equal("date-time", createdAt.GetProperty("format").GetString());
    }
}
