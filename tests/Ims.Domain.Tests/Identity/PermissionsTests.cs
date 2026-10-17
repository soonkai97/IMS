namespace Ims.Domain.Tests.Identity;

/// <summary>Spot checks of what the system roles hold by default against PRD 3.1 (permission matrix) and the role rules.</summary>
public class PermissionsTests
{
    private static bool Holds(string role, string permission) => SystemRoleDefaults.For(role).Contains(permission);

    [Fact]
    public void Admin_HasEveryPermission()
    {
        foreach (var permission in PermissionCatalog.Codes)
            Assert.True(Holds(Roles.Admin, permission), permission);
    }

    [Theory]
    [InlineData(Permissions.SettingsManage)]
    [InlineData(Permissions.UsersManage)]
    [InlineData(Permissions.UsersRead)] // the users list shows e-mail, role and warehouses: Admin only (EA-12)
    public void OnlyAdmin_CanManageSettingsAndUsers(string permission)
    {
        foreach (var role in Roles.All)
            Assert.Equal(role == Roles.Admin, Holds(role, permission));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, false)]          // "--" in PRD 3.1
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.WarehouseStaff, true)]
    [InlineData(Roles.Clerk, true)]
    public void CreateReceiptOrIssue(string role, bool expected)
    {
        Assert.Equal(expected, Holds(role, Permissions.DocumentCreate));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, true)]           // "Yes (large)": by the large tier
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, false)]
    public void ApproveDocuments(string role, bool expected)
    {
        Assert.Equal(expected, Holds(role, Permissions.DocumentApprove) || Holds(role, Permissions.DocumentApproveLarge));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, false)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.WarehouseStaff, true)]  // within no-approval rules (enforced by DocumentActionPolicy)
    [InlineData(Roles.Clerk, false)]
    public void PostDocuments(string role, bool expected)
    {
        Assert.Equal(expected, Holds(role, Permissions.DocumentPost));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, false)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, false)]
    public void ReversePostedDocuments(string role, bool expected)
    {
        Assert.Equal(expected, Holds(role, Permissions.DocumentReverse));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, true)]
    [InlineData(Roles.Supervisor, false)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, false)]
    public void ReadAuditLog(string role, bool expected)
    {
        Assert.Equal(expected, Holds(role, Permissions.AuditRead));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, true)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, false)]
    public void ReadReports(string role, bool expected)
    {
        Assert.Equal(expected, Holds(role, Permissions.ReportsRead));
    }

    [Fact]
    public void EveryRoleCanViewStock()
    {
        foreach (var role in Roles.All)
            Assert.True(Holds(role, Permissions.StockRead), role);
    }

    [Fact]
    public void OwnerIsReadOnlyApart_FromApproving()
    {
        var owner = SystemRoleDefaults.For(Roles.Owner);

        Assert.DoesNotContain(Permissions.MasterDataManage, owner);
        Assert.DoesNotContain(Permissions.DocumentCreate, owner);
        Assert.DoesNotContain(Permissions.DocumentPost, owner);
        Assert.Contains(Permissions.DocumentApproveLarge, owner);
        Assert.DoesNotContain(Permissions.DocumentApprove, owner); // the Owner holds only the large tier
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.Owner, false)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, false)]
    public void ManageMasterData(string role, bool expected)
    {
        Assert.Equal(expected, Holds(role, Permissions.MasterDataManage));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.Clerk, true)]           // clerks may add a partner, not change one
    [InlineData(Roles.Owner, false)]
    [InlineData(Roles.WarehouseStaff, false)]
    public void CreatePartners(string role, bool expected)
    {
        Assert.Equal(expected, Holds(role, Permissions.PartnersCreate));
    }

    [Fact]
    public void Clerk_CanCreatePartners_ButNotEditOrDeactivateThem()
    {
        var clerk = SystemRoleDefaults.For(Roles.Clerk);

        Assert.Contains(Permissions.PartnersCreate, clerk);
        Assert.DoesNotContain(Permissions.MasterDataManage, clerk);   // edit and deactivate need MasterDataManage
    }

    [Fact]
    public void EveryRoleThatManagesMasterData_AlsoCreatesPartners()
    {
        foreach (var role in Roles.All.Where(r => Holds(r, Permissions.MasterDataManage)))
            Assert.True(Holds(role, Permissions.PartnersCreate), role);
    }

    [Theory]
    [InlineData(Roles.Admin, true, true)]
    [InlineData(Roles.Owner, true, true)]
    [InlineData(Roles.Supervisor, true, true)]
    [InlineData(Roles.Clerk, true, false)]           // cost on the documents they work on, not what the stock is worth
    [InlineData(Roles.WarehouseStaff, false, false)] // no cost or value anywhere
    public void CostAndValuationVisibility(string role, bool cost, bool valuation)
    {
        Assert.Equal(cost, Holds(role, Permissions.CostView));
        Assert.Equal(valuation, Holds(role, Permissions.ValuationView));
    }

    [Fact]
    public void WhoeverMaySeeValuation_MaySeeCost()
    {
        foreach (var role in Roles.All.Where(r => Holds(r, Permissions.ValuationView)))
            Assert.True(Holds(role, Permissions.CostView), role);
    }

    [Fact]
    public void UnknownRole_HasNoPermissions()
    {
        Assert.Empty(SystemRoleDefaults.For("Nobody"));
        Assert.False(Holds("Nobody", Permissions.StockRead));
    }

    [Fact]
    public void EveryPermissionGrantedToARole_IsAKnownPermission()
    {
        foreach (var role in Roles.All)
            Assert.All(SystemRoleDefaults.For(role), p => Assert.True(PermissionCatalog.IsKnown(p), p));
    }

    [Fact]
    public void PermissionNames_AreUnique_AndAreTheCatalogsCodes()
    {
        var names = typeof(Permissions).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal(PermissionCatalog.Codes.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
    }

    // ----- Roles -----
    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, true)]
    [InlineData(Roles.Supervisor, false)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, false)]
    public void SeesAllWarehouses_OnlyAdminAndOwner(string role, bool expected)
    {
        Assert.Equal(expected, Roles.SeesAllWarehouses(role));
    }

    [Fact]
    public void Roles_AllListsTheFiveRolesOfThePrd()
    {
        Assert.Equal(
            new[] { "Admin", "Owner", "Supervisor", "WarehouseStaff", "Clerk" }.Order(),
            Roles.All.Order());
    }
}
