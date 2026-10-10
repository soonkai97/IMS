using Ims.Domain;
namespace Ims.Application.Features.Attachments;

/// <summary>
/// Stores an upload as a physical file and records its metadata. The type is decided from the file's own bytes, the stored name is
/// generated, the size is enforced while streaming, and if the database write fails the file is removed again (no orphans).
/// </summary>
public class UploadAttachmentHandler(IAttachmentOwners owners, IFileStorage storage, IRepository<Attachment> attachments, ICurrentUser current,
    IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<UploadAttachmentCommand, AttachmentDto>
{
    public async Task<AttachmentDto> HandleAsync(UploadAttachmentCommand cmd, CancellationToken ct)
    {
        await owners.For(cmd.OwnerType).EnsureCanWriteAsync(cmd.OwnerId, ct);
        if (await attachments.CountAsync(new AttachmentsCountSpec(cmd.OwnerType, cmd.OwnerId), ct) >= Limits.MaxAttachmentsPerOwner)
            throw new ConflictException(Messages.TooManyAttachments(Limits.MaxAttachmentsPerOwner));

        var kind = await DetectKindAsync(cmd.Content, ct);
        var stored = await storage.SaveAsync(cmd.Content, AttachmentRules.ExtensionOf(kind), Limits.MaxAttachmentBytes, ct);
        try
        {
            if (kind == FileKind.Pdf) await EnsureNoActiveContentAsync(stored, ct);
            var attachment = Attachment.Create(cmd.OwnerType, cmd.OwnerId, cmd.FileName, kind, stored.StoragePath, stored.SizeBytes, stored.Sha256,
                current.UserId, clock.UtcNow);
            attachments.Add(attachment);
            await uow.SaveChangesAsync(ct);
            audit.Record(AuditActions.AttachmentUploaded, AuditEntities.Attachment, attachment.Id.ToString(),
                after: new { cmd.OwnerType, cmd.OwnerId, attachment.FileName, attachment.SizeBytes, attachment.Sha256 });
            await uow.SaveChangesAsync(ct);
            return new AttachmentDto(attachment.Id, attachment.OwnerType, attachment.OwnerId, attachment.FileName, attachment.ContentType,
                attachment.SizeBytes, current.Username, attachment.UploadedAt);
        }
        catch
        {
            await storage.DeleteAsync(stored.StoragePath, CancellationToken.None);
            throw;
        }
    }

    private async Task EnsureNoActiveContentAsync(StoredFile stored, CancellationToken ct)
    {
        await using var stream = await storage.OpenReadAsync(stored.StoragePath, ct);
        var bytes = new byte[stored.SizeBytes]; // bounded: the upload limit is 10 MB
        await stream.ReadExactlyAsync(bytes, ct);
        if (AttachmentRules.HasActivePdfContent(bytes)) throw new ValidationException(Messages.FileHasActiveContent);
    }

    private static async Task<FileKind> DetectKindAsync(Stream content, CancellationToken ct)
    {
        if (!content.CanSeek) throw new ValidationException(Messages.FileStreamNotSeekable);
        var header = new byte[Limits.FileHeaderBytes];
        content.Position = 0;
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        content.Position = 0;
        return AttachmentRules.Detect(header.AsSpan(0, read)) ?? throw new ValidationException(Messages.FileTypeNotAllowed);
    }
}
