using System.Net;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>AUTH-02: every endpoint answers 200 for roles that hold its permission and 403 for the others; without a token it is 401.</summary>
[Collection(ApiCollection.Name)]
public class RolePermissionTests(ApiFixture fixture)
{
    public static IEnumerable<object[]> RolesAndEndpoints() =>
        from role in TestUsers.AllRoles
        from endpoint in EndpointCatalog.All
        select new object[] { role, endpoint.Name };

    public static IEnumerable<object[]> Endpoints() => EndpointCatalog.All.Select(e => new object[] { e.Name });

    [Theory]
    [MemberData(nameof(RolesAndEndpoints))]
    public async Task Endpoint_is_open_to_the_roles_that_hold_its_permission_and_403_for_the_rest(string role, string endpoint)
    {
        var @case = EndpointCatalog.Named(endpoint);
        var client = await fixture.AsAsync(role);

        var response = await client.SendAsync(@case.Method, @case.Path(fixture.Reference), @case.Body?.Invoke(fixture.Reference));

        if (!RoleMatrix.MayUse(role, @case.Permission))
        {
            response.ShouldBe(HttpStatusCode.Forbidden);
        }
        else if (@case.OkWhenAllowed)
        {
            response.ShouldBeOk();
        }
        else
        {
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.Status);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.Status);
            Assert.True(response.StatusCode < 500, $"{role} {endpoint} returned {response.StatusCode}: {response.Body}");
        }
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_without_a_token_is_401(string endpoint)
    {
        var @case = EndpointCatalog.Named(endpoint);

        var response = await fixture.Anonymous().SendAsync(@case.Method, @case.Path(fixture.Reference), @case.Body?.Invoke(fixture.Reference));

        response.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("Supervisor")]
    [InlineData("WarehouseStaff")]
    [InlineData("Clerk")]
    public async Task Every_role_can_read_its_own_profile(string role)
    {
        var client = await fixture.AsAsync(role);

        (await client.Auth.MeAsync()).ShouldBeOk();
    }

    [Fact]
    public async Task Health_check_is_open_without_a_token()
    {
        (await fixture.Anonymous().GetAsync("/health")).ShouldBeOk();
    }
}
