using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ims.Api.Tests.Support;
using Ims.Domain;
using Ims.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Ims.Api.Tests.Tests.Access;

/// <summary>The token writer on its own, from snapshots made by hand: a custom role, and the size of the largest token the shapes allow.</summary>
public class TokenServiceTests
{
    private static readonly TokenService Service = new(
        Options.Create(new JwtOptions { Key = new string('k', 48), Issuer = "ims", Audience = "ims-clients" }), new FrozenClock());

    private static AccessSnapshot Snapshot(string role, IEnumerable<string> grants, bool allWarehouses, IEnumerable<int> warehouses) =>
        new(7, role, role, EffectivePermissions.Compute(grants), allWarehouses, warehouses.ToList());

    private static List<string> All(string token, string type) => new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Where(c => c.Type == type).Select(c => c.Value).ToList();

    [Fact]
    public void A_custom_role_gives_the_grants_it_holds_and_nothing_else()
    {
        var token = Service.CreateAccessToken("lena", Snapshot("night-shift", [Permissions.StockRead, Permissions.ReportsRead, "retired.permission"], false, [3, 5]), out _);

        Assert.Equal([Permissions.ReportsRead, Permissions.StockRead], All(token, ClaimNames.Permission).Order(StringComparer.Ordinal));
        Assert.Equal(["night-shift"], All(token, ClaimTypes.Role));
        Assert.Equal([ClaimNames.False], All(token, ClaimNames.AllWarehouses));
        Assert.Equal(["3", "5"], All(token, ClaimNames.Warehouse));
    }

    [Fact]
    public void The_warehouse_list_is_left_out_for_a_user_who_sees_every_warehouse()
    {
        var token = Service.CreateAccessToken("ada", Snapshot(Roles.Admin, PermissionCatalog.Codes, true, [3, 5]), out _);

        Assert.Equal([ClaimNames.True], All(token, ClaimNames.AllWarehouses));
        Assert.Empty(All(token, ClaimNames.Warehouse));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_token_of_an_administrator_with_fifty_warehouses_is_under_two_kilobytes(bool allWarehouses)
    {
        var token = Service.CreateAccessToken("ada", Snapshot(Roles.Admin, PermissionCatalog.Codes, allWarehouses, Enumerable.Range(1000, 50)), out _);

        Assert.Equal(PermissionCatalog.Codes.Count, All(token, ClaimNames.Permission).Count);
        Assert.InRange(Encoding.UTF8.GetByteCount(token), 1, 2047);
    }
}
