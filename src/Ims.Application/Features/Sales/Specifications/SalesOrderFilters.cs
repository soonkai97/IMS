using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

internal static class SalesOrderFilters
{
    /// <summary>Shared by the count and page specs so both always filter identically. <c>customerId</c> and <c>warehouseId</c> are the filters as integer ids (see <c>IdMapExtensions</c>); <c>scope</c> null = all warehouses.</summary>
    public static void Apply(ISpecificationBuilder<SalesOrder> query, SalesOrderFilter f, int? customerId, int? warehouseId, IReadOnlyCollection<int>? scope)
    {
        query.AsNoTracking();
        if (scope is not null) query.Where(o => scope.Contains(o.WarehouseId));
        if (f.Status is { } s) query.Where(o => o.Status == s);
        if (customerId is { } customer) query.Where(o => o.CustomerId == customer);
        if (warehouseId is { } w) query.Where(o => o.WarehouseId == w);
        if (f.From is { } from) { var start = Paging.StartOfDayUtc(from); query.Where(o => o.CreatedAt >= start); }
        if (f.To is { } to) { var end = Paging.EndOfDayUtc(to); query.Where(o => o.CreatedAt < end); }
        if (f.Search?.Trim() is { Length: > 0 } text) query.Where(o => o.No.Contains(text));
    }
}
