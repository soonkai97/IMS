namespace Ims.Application.Features.Partners;

public sealed record UpdatePartnerCommand(Guid Id, PartnerSaveRequest Data) : ICommand<PartnerDto>;
