using Ims.Domain;
namespace Ims.Application.Features.Partners;

/// <param name="Tin">Module accounting. Left out of a save while the module is off (a value is a 400); with the module on, a save replaces both tax-ready fields, so null clears.</param>
public record PartnerSaveRequest(PartnerType Type, string Name, string? Contact, bool IsActive, string? Tin = null, string? SstRegNo = null);
