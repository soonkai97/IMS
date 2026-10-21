using Ims.Application.Features.Purchasing;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class UpdateDocumentHandler(IRepository<StockDocument> documents, IReadRepository<Partner> partners,
    IDocumentLineBuilder lineBuilder, IIssueValuation valuation, IReceiptCostDefaults receiptCosts, IReceiptLinkRules links, IDocumentAccess access, IDocumentReader reader, IDocumentVersion version,
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
        // A receipt created from a purchase order keeps its link for good (nothing in the request can change it): its supplier is the order's, and its lines name the order's lines within what remains.
        var order = doc.IsLinked ? await links.OrderForSaveAsync(doc, partner, ct) : null;
        doc.UpdateHeader(partner, cmd.Data.Purpose, cmd.Data.Remark);
        var lines = await lineBuilder.BuildAsync(doc.Type, cmd.Data.Lines, ct, existing: doc.Lines.ToList(), order);
        if (order is not null) await links.EnsureWithinRemainingAsync(order, lines, ct);
        doc.ReplaceLines(lines);
        await valuation.EstimateAsync(doc, ct);
        await receiptCosts.ApplyAsync(doc, ct);

        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
