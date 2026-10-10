using Ims.Domain;
namespace Ims.Application.Features.Users;

internal static class UserWarehouses
{
    /// <summary>The warehouses a user is assigned to must exist (a crafted request used to reach the database foreign key and answer 500).</summary>
    /// <exception cref="ValidationException">An id does not belong to a warehouse.</exception>
    public static async Task EnsureExistAsync(IReadRepository<Warehouse> warehouses, IReadOnlyList<int>? requested, CancellationToken ct)
    {
        var ids = (requested ?? []).Distinct().ToList();
        if (ids.Count == 0) return;
        var found = await warehouses.ListAsync(new ByIdsSpec<Warehouse>(ids, tracked: false), ct);
        if (found.Count != ids.Count) throw new ValidationException(Messages.WarehouseMissing);
    }
}
