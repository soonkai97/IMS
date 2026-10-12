using Ims.Domain;
namespace Ims.Application.Features.Attachments;

/// <param name="Content">Seekable stream of the uploaded file (the handler reads the header, then the whole stream).</param>
public record UploadAttachmentCommand(AttachmentOwnerType OwnerType, Guid OwnerId, string FileName, Stream Content, long Length) : ICommand<AttachmentDto>;
