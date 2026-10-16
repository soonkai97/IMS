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
}
