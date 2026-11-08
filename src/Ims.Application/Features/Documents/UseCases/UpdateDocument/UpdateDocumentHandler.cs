using Ims.Application.Features.Purchasing;
using Ims.Application.Features.Sales;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class UpdateDocumentHandler(IRepository<StockDocument> documents, IReadRepository<Partner> partners,
    IDocumentLineBuilder lineBuilder, IIssueValuation valuation, IReceiptCostDefaults receiptCosts, IReceiptLinkRules links, ISalesDeliveryRules deliveries, IDocumentAccess access, IDocumentReader reader, IDocumentVersion version,
    IIdMap<Warehouse> warehouseIds, IUnitOfWork uow)
    : ICommandHandler<UpdateDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(UpdateDocumentCommand cmd, CancellationToken ct)
    {
        var doc = await documents.FirstOrDefaultAsync(new DocumentForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureDocumentInScope(doc.WarehouseId);
        access.EnsureMayEditOrSubmit(doc);
        version.EnsureCurrent(doc, cmd.IfMatch);
        if (doc.WarehouseId != await warehouseIds.ToInternalOrNoRowAsync(cmd.Data.WarehouseId, ct)) throw new ValidationException(Messages.WarehouseCannotChange);

        var partner = await CreateDocumentHandler.LoadPartnerAsync(partners, cmd.Data.PartnerId, ct);
        // A document created from an order keeps its link for good (nothing in the request can change it): its partner is the order's, and its lines name the order's lines within what remains.
        LinkedOrder? link = doc.Link.Kind switch
        {
            DocumentLinkKind.PurchaseOrder => new PurchaseLinkedOrder(await links.OrderForSaveAsync(doc, partner, ct)),
            DocumentLinkKind.SalesOrder => new SalesLinkedOrder(await deliveries.OrderForSaveAsync(doc, partner, cmd.Data.Purpose, ct)),
            _ => null
        };
        doc.UpdateHeader(partner, cmd.Data.Purpose, cmd.Data.Remark);
        var lines = await lineBuilder.BuildAsync(doc.Type, cmd.Data.Lines, ct, existing: doc.Lines.ToList(), link);
        if (link is PurchaseLinkedOrder purchase) await links.EnsureWithinRemainingAsync(purchase.Order, lines, ct);
        else if (link is SalesLinkedOrder sales) await deliveries.EnsureWithinRemainingAsync(sales.Order, lines, ct);
        doc.ReplaceLines(lines);
        await valuation.EstimateAsync(doc, ct);
        await receiptCosts.ApplyAsync(doc, ct);

        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
