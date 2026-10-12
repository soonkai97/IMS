using Ims.Domain;
using Microsoft.Extensions.Logging;
namespace Ims.Application.Features.Attachments;

/// <summary>Soft-deletes the metadata (kept for the audit trail), then removes the physical file. A failed file removal is logged, not fatal.</summary>
public class DeleteAttachmentHandler(IRepository<Attachment> attachments, IAttachmentOwners owners, IFileStorage storage,
    IUnitOfWork uow, IAuditTrail audit, ILogger<DeleteAttachmentHandler> log) : ICommandHandler<DeleteAttachmentCommand, Unit>
{
    public async Task<Unit> HandleAsync(DeleteAttachmentCommand cmd, CancellationToken ct)
    {
        var a = await attachments.FirstOrDefaultAsync(new ByPublicIdSpec<Attachment>(cmd.Id), ct) ?? throw new NotFoundException(Messages.AttachmentNotFound);
        try { await owners.For(a.OwnerType).EnsureCanDeleteAsync(a.OwnerId, ct); }
        catch (NotFoundException) { throw new NotFoundException(Messages.AttachmentNotFound); } // an owner the caller cannot see: same answer as a missing file (SEC-11)

        a.Delete();
        audit.Record(AuditActions.AttachmentDeleted, AuditEntities.Attachment, a.PublicId.ToString(), before: new { a.OwnerType, a.FileName });
        await uow.SaveChangesAsync(ct);

        try { await storage.DeleteAsync(a.StoragePath, CancellationToken.None); }
        catch (Exception ex) { log.LogWarning(ex, "Attachment {AttachmentId} was removed but its file {StoragePath} could not be deleted", a.PublicId, a.StoragePath); }
        return Unit.Value;
    }
}
