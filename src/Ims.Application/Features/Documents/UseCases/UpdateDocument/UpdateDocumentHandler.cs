using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class UpdateDocumentHandler(IRepository<StockDocument> documents, IReadRepository<Partner> partners,
    IDocumentLineBuilder lineBuilder, IDocumentAccess access, IDocumentReader reader, IUnitOfWork uow)
    : ICommandHandler<UpdateDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(UpdateDocumentCommand cmd, CancellationToken ct)
    {
        var doc = await documents.FirstOrDefaultAsync(new DocumentForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureDocumentInScope(doc.WarehouseId);
        access.EnsureMayEditOrSubmit(doc);
        DocumentVersion.EnsureCurrent(doc, cmd.IfMatch);
        if (doc.WarehouseId != cmd.Data.WarehouseId) throw new ValidationException(Messages.WarehouseCannotChange);

        var partner = await CreateDocumentHandler.LoadPartnerAsync(partners, cmd.Data.PartnerId, ct);
        doc.UpdateHeader(partner, cmd.Data.Purpose, cmd.Data.Remark);
        doc.ReplaceLines(await lineBuilder.BuildAsync(doc.Type, cmd.Data.Lines, ct));

        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
