namespace Ims.Domain.Tests.Identity;

public class RoleTests
{
    [Fact]
    public void Create_KeepsTheFieldsTrimmedAndHoldsEachPermissionOnce()
    {
        var role = Role.Create(" Packer ", "  Packing floor ", "  Packs orders.  ", false, [Permissions.StockRead, Permissions.DocumentCreate, Permissions.StockRead]);

        Assert.Equal("Packer", role.Code);
        Assert.Equal("Packing floor", role.Name);
        Assert.Equal("Packs orders.", role.Description);
        Assert.False(role.IsSystem);
        Assert.True(role.IsActive);
        Assert.Equal([Permissions.DocumentCreate, Permissions.StockRead], role.Grants.Select(g => g.PermissionCode).Order(StringComparer.Ordinal));
        Assert.NotEqual(Guid.Empty, role.PublicId);
    }

    [Fact]
    public void Create_AMissingDescriptionIsEmpty()
    {
        Assert.Equal("", Role.Create("Packer", "Packer", null, false, []).Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankCode_Throws(string code)
    {
        var ex = Assert.Throws<DomainException>(() => Role.Create(code, "Packer", null, false, []));

        Assert.Equal(Messages.RoleCodeInvalid, ex.Message);
    }

    [Fact]
    public void Create_CodeLongerThanTheColumn_Throws()
    {
        Assert.Equal(Messages.RoleCodeInvalid, Assert.Throws<DomainException>(() => Role.Create(new string('c', Limits.RoleMaxLength + 1), "Packer", null, false, [])).Message);
        Assert.Equal(new string('c', Limits.RoleMaxLength), Role.Create(new string('c', Limits.RoleMaxLength), "Packer", null, false, []).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" a ")]
    public void Create_NameTooShort_Throws(string name)
    {
        var ex = Assert.Throws<DomainException>(() => Role.Create("Packer", name, null, false, []));

        Assert.Equal(Messages.RoleNameInvalid, ex.Message);
    }

    [Fact]
    public void Create_NameBounds_AreTwoAndSixtyCharacters()
    {
        Assert.Equal(new string('n', Limits.RoleNameMinLength), Role.Create("Packer", new string('n', Limits.RoleNameMinLength), null, false, []).Name);
        Assert.Equal(new string('n', Limits.RoleNameMaxLength), Role.Create("Packer", new string('n', Limits.RoleNameMaxLength), null, false, []).Name);
        Assert.Equal(Messages.RoleNameInvalid, Assert.Throws<DomainException>(() => Role.Create("Packer", new string('n', Limits.RoleNameMaxLength + 1), null, false, [])).Message);
    }

    [Fact]
    public void Create_DescriptionLongerThanTheColumn_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => Role.Create("Packer", "Packer", new string('d', Limits.RoleDescriptionMaxLength + 1), false, []));

        Assert.Equal(Messages.RoleDescriptionTooLong, ex.Message);
        Assert.Equal(Limits.RoleDescriptionMaxLength, Role.Create("Packer", "Packer", new string('d', Limits.RoleDescriptionMaxLength), false, []).Description.Length);
    }

    [Fact]
    public void Create_APermissionTheCatalogDoesNotKnow_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => Role.Create("Packer", "Packer", null, false, [Permissions.StockRead, "stock.teleport"]));

        Assert.Equal(Messages.UnknownPermission("stock.teleport"), ex.Message);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public void ASystemRoleBuiltFromItsDefaults_HoldsExactlyThem(string code)
    {
        var role = Role.Create(code, code, null, true, SystemRoleDefaults.For(code));

        Assert.Equal(SystemRoleDefaults.For(code).Order(StringComparer.Ordinal), role.Grants.Select(g => g.PermissionCode).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SetPermissions_ReplacesTheSetAndReportsWhatChangedInCatalogOrder()
    {
        var role = Role.Create("Packer", "Packer", null, false, [Permissions.StockRead, Permissions.DocumentCreate]);

        var (added, removed) = role.SetPermissions([Permissions.StockRead, Permissions.ReportsRead, Permissions.CostView]);

        Assert.Equal([Permissions.ReportsRead, Permissions.CostView], added);
        Assert.Equal([Permissions.DocumentCreate], removed);
        Assert.Equal([Permissions.StockRead, Permissions.ReportsRead, Permissions.CostView], PermissionCatalog.InOrder(role.Grants.Select(g => g.PermissionCode)));
        var (addedAgain, removedAgain) = role.SetPermissions([Permissions.StockRead, Permissions.ReportsRead, Permissions.CostView]); // the same set again changes nothing
        Assert.Empty(addedAgain);
        Assert.Empty(removedAgain);
    }

    [Fact]
    public void SetPermissions_AnUnknownOrDifferentlyCasedCode_ThrowsAndChangesNothing()
    {
        var role = Role.Create("Packer", "Packer", null, false, [Permissions.StockRead]);

        Assert.Equal(Messages.UnknownPermission("Stock.Read"), Assert.Throws<DomainException>(() => role.SetPermissions([Permissions.StockRead, "Stock.Read"])).Message);
        Assert.Equal([Permissions.StockRead], role.Grants.Select(g => g.PermissionCode));
    }

    [Fact]
    public void ASystemRoleCannotBeRenamedDeactivatedOrDeleted_ACustomRoleCanBeAllThree()
    {
        var system = Role.Create(Roles.Clerk, "Clerk", null, true, []);
        var custom = Role.Create("Packer", "Packer", null, false, []);

        Assert.Equal(Messages.SystemRoleProtected, Assert.Throws<LockedRoleException>(() => system.SetActive(false)).Message);
        Assert.Equal(Messages.SystemRoleProtected, Assert.Throws<LockedRoleException>(system.Delete).Message);
        Assert.Equal(Messages.SystemRoleProtected, Assert.Throws<LockedRoleException>(() => system.UpdateText("Other", null)).Message);
        custom.UpdateText("Packers", null);
        custom.SetActive(false);
        custom.Delete();
        Assert.False(custom.IsActive);
        Assert.True(custom.RecDeleted);
    }

    [Fact]
    public void ThePermissionsOfARoleThatHoldsEveryAdminCorePermissionCannotBeReplaced_EvenWithTheSameSet()
    {
        var admin = Role.Create(Roles.Admin, "Administrator", null, true, PermissionCatalog.Codes);
        var owner = Role.Create(Roles.Owner, "Owner", null, true, SystemRoleDefaults.For(Roles.Owner));

        Assert.Equal(Messages.AdminRoleLocked, Assert.Throws<LockedRoleException>(() => admin.SetPermissions([Permissions.StockRead])).Message);
        Assert.Throws<LockedRoleException>(() => admin.SetPermissions(PermissionCatalog.Codes));
        Assert.Equal(PermissionCatalog.Codes.Count, admin.Grants.Count);
        owner.SetPermissions([Permissions.StockRead]);   // the other system roles keep editable permissions
    }

    [Fact]
    public void UpdateText_KeepsTheCodeAndChecksTheName()
    {
        var role = Role.Create("Packer", "Packer", null, false, []);

        role.UpdateText(" Packing floor ", " New text ");

        Assert.Equal(("Packer", "Packing floor", "New text"), (role.Code, role.Name, role.Description));
        Assert.Equal(Messages.RoleNameInvalid, Assert.Throws<DomainException>(() => role.UpdateText("x", null)).Message);
    }

    [Fact]
    public void FirstMissingRequirement_NamesTheCodeAndWhatItNeeds_AndIsNullForAClosedSet()
    {
        Assert.Equal((Permissions.UsersManage, Permissions.UsersRead), PermissionCatalog.FirstMissingRequirement([Permissions.UsersManage]));
        Assert.Null(PermissionCatalog.FirstMissingRequirement([Permissions.UsersManage, Permissions.UsersRead]));
        Assert.Null(PermissionCatalog.FirstMissingRequirement([Permissions.DocumentApproveLarge])); // the tiers need nothing from each other
        Assert.Null(PermissionCatalog.FirstMissingRequirement([Permissions.DocumentApprove]));
    }

    [Theory]
    [InlineData("Night clerk", true)]
    [InlineData("Caf\u00E9 \u4E2D\u6587", true)]
    [InlineData("a\0b", false)]
    [InlineData("a\r\nb", false)]
    [InlineData("a\tb", false)]
    [InlineData("a\u202Eb", false)]   // right-to-left override
    [InlineData("a\u2067b", false)]   // right-to-left isolate
    [InlineData("a\u200Bb", false)]   // zero-width space
    [InlineData("a\u2028b", false)]   // line separator
    public void IsPlainText_RefusesControlFormatAndSeparatorCharacters(string text, bool plain)
    {
        Assert.Equal(plain, Role.IsPlainText(text));
        if (!plain) Assert.Throws<DomainException>(() => Role.Create("Packer", text, null, false, []));
    }

    [Fact]
    public void PermissionsLocked_IsTrueOnlyForASystemRoleThatHoldsEveryAdminCorePermission()
    {
        var adminCore = PermissionCatalog.All.Where(p => p.AdminCore).Select(p => p.Code).ToList();

        Assert.True(Role.Create(SystemRoleCodes.Admin, "Administrator", null, true, PermissionCatalog.Codes).PermissionsLocked);
        Assert.False(Role.Create(SystemRoleCodes.Owner, "Owner", null, true, SystemRoleDefaults.For(SystemRoleCodes.Owner)).PermissionsLocked);
        Assert.False(Role.Create("Copy", "Copy", null, false, PermissionCatalog.Codes).PermissionsLocked);        // custom: never
        Assert.False(Role.Create("Almost", "Almost", null, true, adminCore.Skip(1)).PermissionsLocked);           // missing one
    }
}
