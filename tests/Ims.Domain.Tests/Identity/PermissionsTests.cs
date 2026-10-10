namespace Ims.Domain.Tests.Identity;

/// <summary>Spot checks against PRD 3.1 (permission matrix) and the role rules.</summary>
public class PermissionsTests
{
    [Fact]
    public void Admin_HasEveryPermission()
    {
        foreach (var permission in Permissions.All)
            Assert.True(Permissions.Has(Roles.Admin, permission), permission);
    }

    [Theory]
    [InlineData(Permissions.SettingsManage)]
    [InlineData(Permissions.UsersManage)]
    [InlineData(Permissions.UsersRead)] // the users list shows e-mail, role and warehouses: Admin only (EA-12)
    public void OnlyAdmin_CanManageSettingsAndUsers(string permission)
    {
        foreach (var role in Roles.All)
            Assert.Equal(role == Roles.Admin, Permissions.Has(role, permission));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, false)]          // "--" in PRD 3.1
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.WarehouseStaff, true)]
    [InlineData(Roles.Clerk, true)]
    public void CreateReceiptOrIssue(string role, bool expected)
    {
        Assert.Equal(expected, Permissions.Has(role, Permissions.DocumentCreate));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, true)]           // "Yes (large)"
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, false)]
    public void ApproveDocuments(string role, bool expected)
    {
        Assert.Equal(expected, Permissions.Has(role, Permissions.DocumentApprove));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, false)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.WarehouseStaff, true)]  // within no-approval rules (enforced by DocumentActionPolicy)
    [InlineData(Roles.Clerk, false)]
    public void PostDocuments(string role, bool expected)
    {
        Assert.Equal(expected, Permissions.Has(role, Permissions.DocumentPost));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, false)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, false)]
    public void ReversePostedDocuments(string role, bool expected)
    {
        Assert.Equal(expected, Permissions.Has(role, Permissions.DocumentReverse));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, true)]
    [InlineData(Roles.Supervisor, false)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, false)]
    public void ReadAuditLog(string role, bool expected)
    {
        Assert.Equal(expected, Permissions.Has(role, Permissions.AuditRead));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, true)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, false)]
    public void ReadReports(string role, bool expected)
    {
        Assert.Equal(expected, Permissions.Has(role, Permissions.ReportsRead));
    }

    [Fact]
    public void EveryRoleCanViewStock()
    {
        foreach (var role in Roles.All)
            Assert.True(Permissions.Has(role, Permissions.StockRead), role);
    }

    [Fact]
    public void OwnerIsReadOnlyApart_FromApproving()
    {
        var owner = Permissions.For(Roles.Owner);

        Assert.DoesNotContain(Permissions.MasterDataManage, owner);
        Assert.DoesNotContain(Permissions.DocumentCreate, owner);
        Assert.DoesNotContain(Permissions.DocumentPost, owner);
        Assert.Contains(Permissions.DocumentApprove, owner);
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.Owner, false)]
    [InlineData(Roles.WarehouseStaff, false)]
    [InlineData(Roles.Clerk, false)]
    public void ManageMasterData(string role, bool expected)
    {
        Assert.Equal(expected, Permissions.Has(role, Permissions.MasterDataManage));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Supervisor, true)]
    [InlineData(Roles.Clerk, true)]           // clerks may add a partner, not change one
    [InlineData(Roles.Owner, false)]
    [InlineData(Roles.WarehouseStaff, false)]
    public void CreatePartners(string role, bool expected)
    {
        Assert.Equal(expected, Permissions.Has(role, Permissions.PartnersCreate));
    }

    [Fact]
    public void Clerk_CanCreatePartners_ButNotEditOrDeactivateThem()
    {
        var clerk = Permissions.For(Roles.Clerk);

        Assert.Contains(Permissions.PartnersCreate, clerk);
        Assert.DoesNotContain(Permissions.MasterDataManage, clerk);   // edit and deactivate need MasterDataManage
    }

    [Fact]
    public void EveryRoleThatManagesMasterData_AlsoCreatesPartners()
    {
        foreach (var role in Roles.All.Where(r => Permissions.Has(r, Permissions.MasterDataManage)))
            Assert.True(Permissions.Has(role, Permissions.PartnersCreate), role);
    }

    [Theory]
    [InlineData(Roles.Admin, true, true)]
    [InlineData(Roles.Owner, true, true)]
    [InlineData(Roles.Supervisor, true, true)]
    [InlineData(Roles.Clerk, true, false)]           // cost on the documents they work on, not what the stock is worth
    [InlineData(Roles.WarehouseStaff, false, false)] // no cost or value anywhere
    public void CostAndValuationVisibility(string role, bool cost, bool valuation)
    {
        Assert.Equal(cost, Permissions.Has(role, Permissions.CostView));
        Assert.Equal(valuation, Permissions.Has(role, Permissions.ValuationView));
    }

    [Fact]
    public void WhoeverMaySeeValuation_MaySeeCost()
    {
        foreach (var role in Roles.All.Where(r => Permissions.Has(r, Permissions.ValuationView)))
            Assert.True(Permissions.Has(role, Permissions.CostView), role);
    }

    [Fact]
    public void UnknownRole_HasNoPermissions()
    {
        Assert.Empty(Permissions.For("Nobody"));
        Assert.False(Permissions.Has("Nobody", Permissions.StockRead));
    }

    [Fact]
    public void EveryPermissionGrantedToARole_IsAKnownPermission()
    {
        foreach (var role in Roles.All)
            Assert.All(Permissions.For(role), p => Assert.Contains(p, Permissions.All));
    }

    [Fact]
    public void PermissionNames_AreUnique()
    {
        Assert.Equal(Permissions.All.Length, Permissions.All.Distinct().Count());
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
