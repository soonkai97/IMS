namespace Ims.Domain;

/// <summary>The header and the items of a saved price list were replaced (<c>Update</c>, then <c>ReplaceItems</c>: one edit, one event). Carries the header before and after and the prices that changed, captured before the items were overwritten.</summary>
public sealed record PriceListUpdated(Guid ListId, PriceListSummary Before, PriceListSummary After, IReadOnlyList<PriceChange> Changes) : IDomainEvent;
