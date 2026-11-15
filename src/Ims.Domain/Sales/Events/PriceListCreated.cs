namespace Ims.Domain;

/// <summary>A price list was created: raised when its items are in (<c>ReplaceItems</c> of a list made by <c>Create</c>), because the audit row of a create counts the items and names every price. Every item is an added price.</summary>
public sealed record PriceListCreated(Guid ListId, PriceListSummary Summary, IReadOnlyList<PriceChange> Changes) : IDomainEvent;
