using Ims.Domain;
using Microsoft.Extensions.Logging;
namespace Ims.Application.Features.Attachments;

/// <summary>
/// Stores an upload as a physical file and records its metadata. The upload is read once into memory (bounded by the size limit) so
/// every check sees the same bytes: the type is decided from the file's own bytes, images lose their metadata (EXIF, GPS, XMP, anything
/// appended after the image), PDFs with active content are refused, and the upload scanner gets the last word before anything touches
/// storage. The stored name is generated, and if the database write fails the file is removed again (no orphans).
/// </summary>
public class UploadAttachmentHandler(IAttachmentOwners owners, IFileStorage storage, IUploadScanner scanner, IRepository<Attachment> attachments,
    ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit, ILogger<UploadAttachmentHandler> log)
    : ICommandHandler<UploadAttachmentCommand, AttachmentDto>
{
    public async Task<AttachmentDto> HandleAsync(UploadAttachmentCommand cmd, CancellationToken ct)
    {
        var policy = owners.For(cmd.OwnerType);
        var ownerId = await policy.ResolveAsync(cmd.OwnerId, ct); // the owner is looked up among the owners of the type asked for: a wrong-type id is simply not found
        await policy.EnsureCanWriteAsync(ownerId, ct);
        if (await attachments.CountAsync(new AttachmentsCountSpec(cmd.OwnerType, ownerId), ct) >= Limits.MaxAttachmentsPerOwner)
            throw new ConflictException(Messages.TooManyAttachments(Limits.MaxAttachmentsPerOwner));

        var upload = await ReadBoundedAsync(cmd.Content, ct);
        var kind = AttachmentRules.Detect(upload.AsSpan(0, Math.Min(upload.Length, Limits.FileHeaderBytes))) ?? throw new ValidationException(Messages.FileTypeNotAllowed);
        var content = Sanitise(kind, upload);
        await EnsureCleanAsync(content, kind, cmd, ct);

        using var stream = new MemoryStream(content, writable: false);
        var stored = await storage.SaveAsync(stream, AttachmentRules.ExtensionOf(kind), Limits.MaxAttachmentBytes, ct);
        try
        {
            var attachment = Attachment.Create(cmd.OwnerType, ownerId, cmd.FileName, kind, stored.StoragePath, stored.SizeBytes, stored.Sha256,
                current.UserId, clock.UtcNow);
            attachments.Add(attachment);
            await uow.SaveChangesAsync(ct);
            audit.Record(AuditActions.AttachmentUploaded, AuditEntities.Attachment, attachment.PublicId.ToString(),
                after: new { cmd.OwnerType, cmd.OwnerId, attachment.FileName, attachment.SizeBytes, attachment.Sha256 });
            await uow.SaveChangesAsync(ct);
            return new AttachmentDto(attachment.PublicId, attachment.OwnerType, cmd.OwnerId, attachment.FileName, attachment.ContentType,
                attachment.SizeBytes, current.Username, attachment.UploadedAt);
        }
        catch
        {
            await storage.DeleteAsync(stored.StoragePath, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Images are rewritten without metadata; a PDF is kept byte for byte but refused when it can run code or carry files.</summary>
    private static byte[] Sanitise(FileKind kind, byte[] upload)
    {
        if (kind != FileKind.Pdf) return ImageMetadataStripper.Strip(kind, upload) ?? throw new ValidationException(Messages.FileNotAValidImage);
        if (AttachmentRules.HasActivePdfContent(upload)) throw new ValidationException(Messages.FileHasActiveContent);
        return upload;
    }

    private async Task EnsureCleanAsync(byte[] content, FileKind kind, UploadAttachmentCommand cmd, CancellationToken ct)
    {
        using var stream = new MemoryStream(content, writable: false);
        var verdict = await scanner.ScanAsync(new UploadScanRequest(stream, AttachmentRules.SafeDisplayName(cmd.FileName, kind), kind, content.Length), ct);
        if (verdict.IsClean) return;
        log.LogWarning("Upload by user {UserId} to {OwnerType} {OwnerId} rejected by the upload scanner: {Reason}", current.UserId, cmd.OwnerType, cmd.OwnerId, verdict.Reason);
        throw new ValidationException(Messages.FileRejectedByScanner);
    }

    /// <summary>Reads the whole upload but never more than the size limit (plus one byte to detect an oversized stream whose length was understated).</summary>
    private static async Task<byte[]> ReadBoundedAsync(Stream content, CancellationToken ct)
    {
        if (content.CanSeek) content.Position = 0;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > Limits.MaxAttachmentBytes) throw new ValidationException(Messages.FileTooLarge(Limits.MaxAttachmentBytes));
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
