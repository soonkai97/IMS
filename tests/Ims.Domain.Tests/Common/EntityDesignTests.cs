using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Common;

public class EntityDesignTests
{
    [Fact]
    public void LedgerEntry_Create_CopiesWarehouseAndSourceFromTheDocument()
    {
        var doc = NewReceipt(9, (NewProduct(), 1m, 1m));

        var entry = StockLedgerEntry.Create(doc, productId: 5, qtyDelta: -2m, unitCost: 3m, LedgerReason.Issue, createdBy: 77, Now);

        Assert.Equal(doc.WarehouseId, entry.WarehouseId);
        Assert.Equal(9, entry.SourceDocId);
        Assert.Equal(5, entry.ProductId);
        Assert.Equal(-2m, entry.QtyDelta);
        Assert.Equal(3m, entry.UnitCost);
        Assert.Equal(LedgerReason.Issue, entry.Reason);
        Assert.Equal(77, entry.CreatedBy);
        Assert.Equal(Now, entry.OccurredAt);
    }

    [Fact]
    public void AuditLog_Record_KeepsAllFields()
    {
        var log = AuditLog.Record("alice", "post", "StockDocument", "RCV-1", "{}", "{\"a\":1}", Now);

        Assert.Equal("alice", log.Actor);
        Assert.Equal("post", log.Action);
        Assert.Equal("StockDocument", log.EntityType);
        Assert.Equal("RCV-1", log.EntityId);
        Assert.Equal("{}", log.Before);
        Assert.Equal("{\"a\":1}", log.After);
        Assert.Equal(Now, log.At);
    }

    [Fact]
    public void SystemSetting_ChangeValue_Replaces()
    {
        var setting = SystemSetting.Create(SettingKeys.AllowNegativeStock, "false");

        setting.ChangeValue("true");

        Assert.Equal("true", setting.Value);
    }

    [Fact]
    public void DomainTypes_DoNotExposePublicConstructors()
    {
        var offenders = typeof(Entity).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic && !t.IsSealed)
            .Where(t => typeof(Entity).IsAssignableFrom(t) || t == typeof(StockDocument) || t == typeof(StockDocumentLine) || t == typeof(StockBalance))
            .Where(t => t.GetConstructors().Length > 0)
            .Select(t => t.Name)
            .ToList();

        Assert.Empty(offenders);
    }
}
