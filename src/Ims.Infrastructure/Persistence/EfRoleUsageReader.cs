using Ims.Application.Features.Roles;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ims.Infrastructure.Persistence;

/// <summary>One grouped count over the indexed <c>User.RoleId</c>, however many roles are asked for. Soft-deleted users are not counted (the global filter).</summary>
public sealed class EfRoleUsageReader(AppDbContext db) : IRoleUsageReader
{
    public async Task<IReadOnlyDictionary<int, int>> CountUsersAsync(IReadOnlyCollection<int> roleIds, CancellationToken ct) =>
        await db.Set<User>().AsNoTracking().Where(u => roleIds.Contains(u.RoleId)).GroupBy(u => u.RoleId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
}
