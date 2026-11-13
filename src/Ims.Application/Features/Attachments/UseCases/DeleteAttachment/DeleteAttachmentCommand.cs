namespace Ims.Application.Features.Attachments;

public sealed record DeleteAttachmentCommand(Guid Id) : ICommand<Unit>;
