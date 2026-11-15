namespace Ims.Domain;

/// <summary>A price list is being deleted (<c>Delete</c>). Carries the header and every item as a removed price, because the audit row of a delete names them.</summary>
public sealed record PriceListDeleted(Guid ListId, PriceListSummary Summary, IReadOnlyList<PriceChange> Changes) : IDomainEvent;
