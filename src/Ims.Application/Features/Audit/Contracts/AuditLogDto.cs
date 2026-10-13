using System.Text.Json.Serialization;
namespace Ims.Application.Features.Audit;

/// <param name="Key">The internal row id, the position the seek cursor is made from. Never in the JSON.</param>
/// <param name="Before">JSON. A key that names a cost or value is left out for a caller without the permission that unlocks it.</param>
/// <param name="After">JSON, with the same rule as <paramref name="Before"/>.</param>
public record AuditLogDto([property: JsonIgnore] long Key, string Actor, string Action, string EntityType, string EntityId,
    [property: ScrubsRestrictedKeys] string? Before, [property: ScrubsRestrictedKeys] string? After, DateTime At);
