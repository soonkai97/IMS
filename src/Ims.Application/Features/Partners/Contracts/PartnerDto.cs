using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Partners;

/// <param name="Tin">Module accounting: left out of the response (not even a null) while the module is off, and when the partner has none.</param>
public record PartnerDto(Guid Id, PartnerType Type, string Name, string? Contact, bool IsActive,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tin = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SstRegNo = null);
