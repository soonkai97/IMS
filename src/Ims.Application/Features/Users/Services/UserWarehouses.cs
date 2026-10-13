using Ims.Domain;
namespace Ims.Application.Features.Users;

internal static class UserWarehouses
{
    /// <summary>
    /// The warehouses a user is assigned to must exist (a crafted request used to reach the database foreign key and answer 500). The request names them by public id;
    /// the answer is their integer ids, which is what the data scope keeps.
    /// </summary>
    /// <exception cref="ValidationException">An id does not belong to a warehouse.</exception>
    public static async Task<List<int>> ResolveAsync(IIdMap<Warehouse> warehouses, IReadOnlyList<Guid>? requested, CancellationToken ct)
    {
        var wanted = (requested ?? []).Distinct().ToList();
        if (wanted.Count == 0) return [];
        var found = await warehouses.ToInternalAsync(wanted, ct);
        if (found.Count != wanted.Count) throw new ValidationException(Messages.WarehouseMissing);
        return found.Values.ToList();
    }
}
