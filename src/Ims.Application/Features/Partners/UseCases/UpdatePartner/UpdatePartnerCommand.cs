namespace Ims.Application.Features.Partners;

public record UpdatePartnerCommand(int Id, PartnerSaveRequest Data) : ICommand<PartnerDto>;
