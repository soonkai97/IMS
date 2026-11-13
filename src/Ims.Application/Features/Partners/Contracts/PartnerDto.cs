using Ims.Domain;
namespace Ims.Application.Features.Partners;

public sealed record PartnerDto(Guid Id, PartnerType Type, string Name, string? Contact, bool IsActive);
