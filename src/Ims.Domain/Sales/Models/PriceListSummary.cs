namespace Ims.Domain;

/// <summary>What the audit trail prints about a price list header: its name, whether it is the default list, its customer (the internal key) and how many items it has. A fact, not an entity.</summary>
public sealed record PriceListSummary(string Name, bool IsDefault, int? CustomerId, int ItemCount);
