using Ims.Application;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Access;

/// <summary>A loader that counts its reads and answers from a function, optionally after a gate opens. Stands in for the database in the cache tests.</summary>
public sealed class CountingLoader(Func<int, int, AccessSnapshot?> answer, Task? gate = null) : IAccessSnapshotLoader
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public async Task<AccessSnapshot?> LoadAsync(int userId, CancellationToken ct)
    {
        var call = Interlocked.Increment(ref _calls);
        if (gate is not null) await gate;
        return answer(userId, call);
    }

    public static AccessSnapshot Snapshot(int userId, string role = Roles.Clerk) =>
        new(userId, role, role, EffectivePermissions.Compute(SystemRoleDefaults.For(role)), false, [1]);
}
