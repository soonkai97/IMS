namespace Ims.Domain.Tests.Warehousing;

public class WarehouseTests
{
    [Fact]
    public void Create_UppercasesAndTrimsCodeAndTrimsName()
    {
        var warehouse = Warehouse.Create("  wh1 ", "  Main  ", "012", "notes");

        Assert.Equal("WH1", warehouse.Code);
        Assert.Equal("Main", warehouse.Name);
        Assert.Equal("012", warehouse.Contact);
        Assert.Equal("notes", warehouse.Notes);
        Assert.True(warehouse.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankCode_Throws(string code)
    {
        var ex = Assert.Throws<DomainException>(() => Warehouse.Create(code, "Main"));

        Assert.Equal(Messages.WarehouseCodeRequired, ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankName_Throws(string name)
    {
        var ex = Assert.Throws<DomainException>(() => Warehouse.Create("WH1", name));

        Assert.Equal(Messages.WarehouseNameRequired, ex.Message);
    }

    [Fact]
    public void EnsureUsable_Active_DoesNotThrow()
    {
        Warehouse.Create("WH1", "Main").EnsureUsable();
    }

    [Fact]
    public void EnsureUsable_Inactive_Throws()
    {
        var warehouse = Warehouse.Create("WH1", "Main", isActive: false);

        var ex = Assert.Throws<DomainException>(warehouse.EnsureUsable);

        Assert.Equal(Messages.WarehouseInactive, ex.Message);
    }

    [Fact]
    public void Update_CanDeactivate()
    {
        var warehouse = Warehouse.Create("WH1", "Main");

        warehouse.Update("WH1", "Main", null, null, isActive: false);

        Assert.Throws<DomainException>(warehouse.EnsureUsable);
    }

    [Fact]
    public void Update_Invalid_LeavesWarehouseUntouched()
    {
        var warehouse = Warehouse.Create("WH1", "Main");

        Assert.Throws<DomainException>(() => warehouse.Update("WH2", " ", null, null, true));

        Assert.Equal("WH1", warehouse.Code);
    }
}
