namespace Ims.Application.Features.Attachments;

public record DeleteAttachmentCommand(Guid Id) : ICommand<Unit>;
