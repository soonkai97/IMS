namespace Ims.Domain;

/// <summary>What the audit trail prints about a sales order when it is created or updated: its status, warehouse, customer, how many lines it has and the total of its selling amounts. A fact, not an entity.</summary>
public sealed record SalesOrderSummary(SalesOrderStatus Status, int WarehouseId, int CustomerId, int LineCount, Money SellingAmount);
