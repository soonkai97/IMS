namespace Ims.Domain.Tests.Approvals;

/// <summary>DDD-10: which module owns an approval-rule subject is a fact of the Domain (it was in Application, next to the module switches it is asked with).</summary>
public class ApprovalRuleModulesTests
{
    [Theory]
    [InlineData(DocumentType.PurchaseOrder, ModuleCodes.Purchasing)]
    [InlineData(DocumentType.SalesOrder, ModuleCodes.Sales)]
    [InlineData(DocumentType.Receipt, null)]
    [InlineData(DocumentType.Issue, null)]
    [InlineData(DocumentType.Transfer, null)]
    [InlineData(DocumentType.Adjust, null)]
    public void ModuleOf_IsTheOwnerOfAnOrderType_AndNullForAStockDocument(DocumentType type, string? module)
    {
        Assert.Equal(module, ApprovalRuleModules.ModuleOf(type));
        Assert.Equal(module is not null, ApprovalRuleModules.IsOrderType(type));
    }

    [Fact]
    public void EveryDocumentType_IsAnswered()
    {
        foreach (var type in Enum.GetValues<DocumentType>())
            Assert.Equal(ApprovalRuleChangeTable.IsOrder(type), ApprovalRuleModules.IsOrderType(type));
    }
}
