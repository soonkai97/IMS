namespace Ims.Application.Features.Partners;

public record CreatePartnerCommand(PartnerSaveRequest Data) : ICommand<PartnerDto>;
