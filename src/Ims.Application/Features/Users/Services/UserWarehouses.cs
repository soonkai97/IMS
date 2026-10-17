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

    /// <summary>
    /// Whether the saved user sees every warehouse: what the request says, or what the user has now when it says nothing (a new user: <paramref name="current"/> is false).
    /// Scope cannot be escalated (ADR roles-permissions 2.6): only a caller who sees every warehouse may switch the flag on.
    /// </summary>
    /// <exception cref="ForbiddenException">The caller does not see every warehouse and would give the flag to someone who does not have it.</exception>
    public static bool ResolveAllWarehouses(bool? requested, bool current, ICurrentUser caller)
    {
        var wanted = requested ?? current;
        if (wanted && !current && !caller.AllWarehouses) throw new ForbiddenException(Messages.AllWarehousesNotGrantable);
        return wanted;
    }
}
