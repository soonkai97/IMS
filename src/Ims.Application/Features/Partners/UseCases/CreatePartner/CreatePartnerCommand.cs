namespace Ims.Application.Features.Partners;

public sealed record CreatePartnerCommand(PartnerSaveRequest Data) : ICommand<PartnerDto>;
