namespace Ims.Domain.Tests.Identity;

/// <summary>Which permissions are offered while a module is off, and the merge that keeps a role's hidden grants when the role is saved (ADR modules-and-tracking-dimensions 4.3).</summary>
public class ModulePermissionsTests
{
    private static readonly string[] Off = [];
    private static readonly string[] On = [ModuleCodes.Purchasing];

    [Fact]
    public void ACorePermissionIsAlwaysOffered_AModulePermissionOnlyWhileItsModuleIsOn()
    {
        Assert.True(ModulePermissions.IsOffered(Permissions.CostView, Off));
        Assert.False(ModulePermissions.IsOffered(Permissions.PoRead, Off));
        Assert.True(ModulePermissions.IsOffered(Permissions.PoRead, On));
        Assert.False(ModulePermissions.IsOffered(Permissions.PoRead, ["batch"])); // another module being on does not offer it
    }

    [Fact]
    public void NotOfferedAndOffered_SplitTheCodes_InCatalogOrder()
    {
        string[] codes = [Permissions.PoRead, Permissions.CostView, Permissions.StockRead];

        Assert.Equal([Permissions.PoRead], ModulePermissions.NotOffered(codes, Off));
        Assert.Equal([Permissions.StockRead, Permissions.CostView], ModulePermissions.Offered(codes, Off));
        Assert.Empty(ModulePermissions.NotOffered(codes, On));
    }

    [Fact]
    public void TheMerge_KeepsTheHiddenGrantsOfTheRole_WhenTheSubmittedSetNeverMentionedThem()
    {
        var merged = ModulePermissions.Merge([Permissions.StockRead, Permissions.CostView], [Permissions.StockRead, Permissions.CostView, Permissions.PoRead], Off);

        Assert.Equal([Permissions.PoRead, Permissions.StockRead, Permissions.CostView], merged); // catalog order
    }

    [Fact]
    public void TheMerge_IsTheSubmittedSetAsItIs_WhileTheModuleIsOn()
    {
        // On, the editor sees the grant, so leaving it out removes it.
        var merged = ModulePermissions.Merge([Permissions.StockRead], [Permissions.StockRead, Permissions.PoRead], On);

        Assert.Equal([Permissions.StockRead], merged);
    }

    [Fact]
    public void TheMerge_DoesNotGiveAHiddenGrantTheRoleDoesNotHold_AndListsNothingTwice()
    {
        Assert.Equal([Permissions.StockRead], ModulePermissions.Merge([Permissions.StockRead], [Permissions.StockRead], Off));
        Assert.Equal([Permissions.PoRead, Permissions.StockRead], ModulePermissions.Merge([Permissions.StockRead, Permissions.PoRead], [Permissions.PoRead], Off));
    }

    [Fact]
    public void ARoleSavedWithTheMergedSet_ChangesNothingForTheHiddenGrant()
    {
        var role = Role.Create("Buyer", "Buyer", null, isSystem: false, [Permissions.CostView, Permissions.StockRead, Permissions.PoRead]);

        var (added, removed) = role.SetPermissions(ModulePermissions.Merge([Permissions.CostView, Permissions.StockRead, Permissions.ReportsRead], role.Grants.Select(g => g.PermissionCode), Off));

        Assert.Equal([Permissions.ReportsRead], added);
        Assert.Empty(removed);
        Assert.Contains(role.Grants, g => g.PermissionCode == Permissions.PoRead);
    }
}
