namespace Ims.Domain.Tests.Identity;

public class EffectivePermissionsTests
{
    [Fact]
    public void ItHoldsTheGrantsOfTheRole()
    {
        var effective = EffectivePermissions.Compute([Permissions.StockRead, Permissions.DocumentCreate]);

        Assert.True(effective.Has(Permissions.StockRead));
        Assert.True(effective.Has(Permissions.DocumentCreate));
        Assert.False(effective.Has(Permissions.DocumentPost));
    }

    [Fact]
    public void ACodeTheCatalogDoesNotKnow_GrantsNothing()
    {
        var effective = EffectivePermissions.Compute(["stock.read", "stock.delete", "STOCK.READ", ""]);

        Assert.Equal([Permissions.StockRead], effective.Codes);
        Assert.False(effective.Has("stock.delete"));
    }

    [Fact]
    public void ARepeatedGrantCountsOnce_AndTheCodesComeInCatalogOrder()
    {
        var effective = EffectivePermissions.Compute([Permissions.ReportsRead, Permissions.UsersRead, Permissions.ReportsRead]);

        Assert.Equal([Permissions.UsersRead, Permissions.ReportsRead], effective.Codes);
    }

    [Fact]
    public void NoGrants_IsNoPermissions()
    {
        Assert.Empty(EffectivePermissions.Compute([]).Codes);
        Assert.Empty(EffectivePermissions.None.Codes);
        Assert.False(EffectivePermissions.None.Has(Permissions.StockRead));
    }

    [Fact]
    public void ActorRights_ForwardWhatThePermissionsHold()
    {
        var rights = new ActorRights(7, EffectivePermissions.Compute([Permissions.StockRead]));

        Assert.Equal(7, rights.UserId);
        Assert.True(rights.Has(Permissions.StockRead));
        Assert.False(rights.Has(Permissions.CostView));
    }

    [Fact]
    public void TheDefaultsOfASystemRole_HandTheSameRightsToEveryCaller()
    {
        var first = SystemRoleDefaults.RightsFor(Roles.Clerk, 1);
        var second = SystemRoleDefaults.RightsFor(Roles.Clerk, 2);

        Assert.Same(first.Permissions, second.Permissions);
        Assert.Equal(1, first.UserId);
        Assert.Equal(2, second.UserId);
    }
}
