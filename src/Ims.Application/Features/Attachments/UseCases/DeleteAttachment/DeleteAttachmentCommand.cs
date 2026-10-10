namespace Ims.Application.Features.Attachments;

public record DeleteAttachmentCommand(int Id) : ICommand<Unit>;
