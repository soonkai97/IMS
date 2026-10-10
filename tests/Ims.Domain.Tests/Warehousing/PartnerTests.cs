namespace Ims.Domain.Tests.Warehousing;

public class PartnerTests
{
    [Fact]
    public void Create_TrimsName()
    {
        var partner = Partner.Create(PartnerType.Supplier, "  Acme  ", "contact");

        Assert.Equal("Acme", partner.Name);
        Assert.Equal(PartnerType.Supplier, partner.Type);
        Assert.Equal("contact", partner.Contact);
        Assert.True(partner.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankName_Throws(string name)
    {
        var ex = Assert.Throws<DomainException>(() => Partner.Create(PartnerType.Customer, name));

        Assert.Equal(Messages.PartnerNameRequired, ex.Message);
    }

    [Theory]
    [InlineData(PartnerType.Supplier, DocumentType.Receipt, true)]
    [InlineData(PartnerType.Customer, DocumentType.Receipt, false)]
    [InlineData(PartnerType.Customer, DocumentType.Issue, true)]
    [InlineData(PartnerType.Supplier, DocumentType.Issue, false)]
    public void EnsureUsableFor_ReceiptsNeedSuppliersAndIssuesNeedCustomers(PartnerType partnerType, DocumentType documentType, bool usable)
    {
        var partner = Partner.Create(partnerType, "P");

        if (usable)
        {
            partner.EnsureUsableFor(documentType);
            return;
        }

        var expected = documentType == DocumentType.Receipt ? PartnerType.Supplier : PartnerType.Customer;
        var ex = Assert.Throws<DomainException>(() => partner.EnsureUsableFor(documentType));
        Assert.Equal(Messages.PartnerWrongType(documentType, expected), ex.Message);
    }

    [Fact]
    public void EnsureUsableFor_Inactive_ThrowsBeforeTypeCheck()
    {
        var partner = Partner.Create(PartnerType.Customer, "P", isActive: false);

        var ex = Assert.Throws<DomainException>(() => partner.EnsureUsableFor(DocumentType.Receipt));

        Assert.Equal(Messages.PartnerInactive, ex.Message);
    }

    [Fact]
    public void Update_ChangesTypeNameAndActive()
    {
        var partner = Partner.Create(PartnerType.Customer, "P");

        partner.Update(PartnerType.Supplier, "Q", null, false);

        Assert.Equal(PartnerType.Supplier, partner.Type);
        Assert.Equal("Q", partner.Name);
        Assert.False(partner.IsActive);
    }
}
