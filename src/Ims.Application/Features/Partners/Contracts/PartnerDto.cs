using Ims.Domain;
namespace Ims.Application.Features.Partners;

public record PartnerDto(int Id, PartnerType Type, string Name, string? Contact, bool IsActive);
