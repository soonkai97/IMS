namespace Ims.Domain.Tests.Catalog;

public class UomAndCategoryTests
{
    [Fact]
    public void Uom_Create_UppercasesAndTrimsCodeAndDefaultsNameToCode()
    {
        var uom = Uom.Create("  ctn ");

        Assert.Equal("CTN", uom.Code);
        Assert.Equal("CTN", uom.Name);
    }

    [Fact]
    public void Uom_Create_KeepsAnExplicitName()
    {
        Assert.Equal("Carton", Uom.Create("ctn", " Carton ").Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Uom_BlankCode_Throws(string code)
    {
        var ex = Assert.Throws<DomainException>(() => Uom.Create(code));

        Assert.Equal(Messages.UomCodeRequired, ex.Message);
    }

    [Fact]
    public void Category_Create_TrimsName()
    {
        Assert.Equal("Drinks", Category.Create("  Drinks ").Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Category_BlankName_Throws(string name)
    {
        var ex = Assert.Throws<DomainException>(() => Category.Create(name));

        Assert.Equal(Messages.CategoryNameRequired, ex.Message);
    }

    [Fact]
    public void Category_Rename_BlankKeepsOldName()
    {
        var category = Category.Create("Drinks");

        Assert.Throws<DomainException>(() => category.Rename(" "));

        Assert.Equal("Drinks", category.Name);
    }

    [Fact]
    public void ProductBarcode_Create_TrimsTheCode()
    {
        Assert.Equal("123", ProductBarcode.Create(" 123 ").Barcode);
    }
}
