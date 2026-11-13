using Ims.Domain;
namespace Ims.Application.Features.Partners;

public sealed record PartnerSaveRequest(PartnerType Type, string Name, string? Contact, bool IsActive);
