using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class CreateDocumentHandler(IRepository<StockDocument> documents, IReadRepository<Warehouse> warehouses, IReadRepository<Partner> partners,
    IDocumentNumbering numbering, IDocumentLineBuilder lineBuilder, IIssueValuation valuation, IReceiptCostDefaults receiptCosts, IDocumentAccess access, IDocumentReader reader,
    ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<CreateDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(CreateDocumentCommand cmd, CancellationToken ct)
    {
        // The whole body re-runs if two requests race for the same document number.
        var publicId = await uow.ExecuteAsync(async () =>
        {
            var r = cmd.Data;
            var warehouse = await warehouses.FirstOrDefaultAsync(new ByPublicIdSpec<Warehouse>(r.WarehouseId), ct) ?? throw new ValidationException(Messages.WarehouseMissing);
            access.EnsureWarehouse(warehouse.Id);
            var partner = await LoadPartnerAsync(partners, r.PartnerId, ct);

            // The aggregate checks the warehouse and partner are usable; lines convert units and cost themselves.
            var doc = StockDocument.Create(cmd.Type, warehouse, partner, r.Purpose, r.Remark, current.UserId, clock.UtcNow);
            doc.AssignNumber(await numbering.NextAsync(cmd.Type, warehouse, ct));
            doc.AddLines(await lineBuilder.BuildAsync(cmd.Type, r.Lines, ct));
            await valuation.EstimateAsync(doc, ct);
            await receiptCosts.ApplyAsync(doc, ct);

            documents.Add(doc);
            audit.Record(AuditActions.DocumentCreated, AuditEntities.StockDocument, doc.No, after: new { doc.Type, doc.WarehouseId, Lines = doc.Lines.Count });
            await uow.SaveChangesAsync(ct);
            return doc.PublicId;
        }, ct);
        return await reader.GetAsync(publicId, ct);
    }

    internal static async Task<Partner?> LoadPartnerAsync(IReadRepository<Partner> partners, Guid? partnerId, CancellationToken ct) =>
        partnerId is { } id ? await partners.FirstOrDefaultAsync(new ByPublicIdSpec<Partner>(id), ct) ?? throw new ValidationException(Messages.PartnerMissing) : null;
}
