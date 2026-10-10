namespace Ims.Application.Features.Audit;

/// <param name="Before">JSON. A key that names a cost or value is left out for a caller without the permission that unlocks it.</param>
/// <param name="After">JSON, with the same rule as <paramref name="Before"/>.</param>
public record AuditLogDto(long Id, string Actor, string Action, string EntityType, string EntityId,
    [property: ScrubsRestrictedKeys] string? Before, [property: ScrubsRestrictedKeys] string? After, DateTime At);
