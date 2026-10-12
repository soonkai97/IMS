namespace Ims.Application.Features.Partners;

public record UpdatePartnerCommand(Guid Id, PartnerSaveRequest Data) : ICommand<PartnerDto>;
