using Ims.Domain;
namespace Ims.Application.Features.Partners;

public record PartnerDto(Guid Id, PartnerType Type, string Name, string? Contact, bool IsActive);
