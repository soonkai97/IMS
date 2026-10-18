using System.Net;
using Ims.Api.Tests.Harness;

namespace Ims.Api.Tests.Tests.MasterData;

/// <summary>Master data is deactivated, never deleted: the API has no delete endpoint for any of it.</summary>
[Collection(ApiCollection.Name)]
public class DeactivateOnlyTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("/api/products")]
    [InlineData("/api/categories")]
    [InlineData("/api/units")]
    [InlineData("/api/warehouses")]
    [InlineData("/api/partners")]
    [InlineData("/api/users")]
    [InlineData("/api/approval-rules")]
    [InlineData("/api/documents")]
    public async Task DELETE_on_a_resource_is_not_allowed(string collection)
    {
        var admin = await fixture.AsAsync("Admin");

        var response = await admin.DeleteAsync($"{collection}/{fixture.Reference.DefaultWarehouseId}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.Status);
    }

    [Fact]
    public async Task The_published_api_description_contains_no_delete_operation()
    {
        var response = await fixture.Anonymous().GetAsync("/openapi/v1.json");

        response.ShouldBeOk();
        using var document = response.Json();
        var deletes = document.RootElement.GetProperty("paths").EnumerateObject()
            .Where(path => !path.Name.StartsWith("/api/attachments") && !path.Name.StartsWith("/api/roles") && path.Value.TryGetProperty("delete", out _)) // attachments are files; a custom role is configuration, soft-deleted when no user holds it (RP-5)
            .Select(path => path.Name).ToList();
        Assert.Empty(deletes);
    }
}
