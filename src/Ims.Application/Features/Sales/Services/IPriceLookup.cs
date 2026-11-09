namespace Ims.Application.Features.Sales;

public interface IPriceLookup
{
    /// <summary>
    /// The list price of each (product, unit) for a customer, per item: the customer's list item if the customer's list has one, else the default list's item, else nothing (absent from the answer). Both lists
    /// must be live (a deleted list is not asked); a unit is exact and nothing is converted. Two reads however many keys.
    /// </summary>
    Task<IReadOnlyDictionary<(int ProductId, int UomId), ListedPrice>> FindAsync(int customerId, IReadOnlyCollection<(int ProductId, int UomId)> keys, CancellationToken ct);
}
