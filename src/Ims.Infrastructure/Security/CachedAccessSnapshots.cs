using Ims.Application;
using Ims.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace Ims.Infrastructure.Security;

/// <summary>
/// The access snapshots in memory, one per user, each valid for <see cref="AccessOptions.SnapshotTtlSeconds"/> (absolute, not sliding: a busy user is re-read too).
/// Concurrency: a hit is one lock-free lookup; a miss takes a short lock only to publish a lazy load, so six parallel requests of one user cause one
/// database read, and no lock is ever held while awaiting. The load does not use the first caller's cancellation token (the others share it). A failed load is
/// thrown to every waiter and removed, so it is never cached and never allows; a missing user is cached like any answer (as null) for the same time.
/// <see cref="Forget"/> must run after the commit it follows: a read that starts afterwards sees the new data, and a read that was already running cannot put
/// its older answer back, because its entry is gone.
/// </summary>
public sealed class CachedAccessSnapshots(IMemoryCache cache, IServiceScopeFactory scopes, IOptions<AccessOptions> options) : IAccessSnapshots
{
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(options.Value.SnapshotTtlSeconds);
    private readonly object _publish = new();
    private long _generation;

    private readonly record struct Key(long Generation, int UserId);

    public async Task<AccessSnapshot?> GetAsync(int userId, CancellationToken ct)
    {
        var key = new Key(Interlocked.Read(ref _generation), userId);
        if (!cache.TryGetValue(key, out Lazy<Task<AccessSnapshot?>>? entry))
        {
            lock (_publish)
            {
                if (!cache.TryGetValue(key, out entry))
                {
                    entry = new Lazy<Task<AccessSnapshot?>>(() => LoadAsync(key), LazyThreadSafetyMode.ExecutionAndPublication);
                    cache.Set(key, entry, _ttl);
                }
            }
        }
        return await entry!.Value.WaitAsync(ct);
    }

    public void Forget(int userId) => cache.Remove(new Key(Interlocked.Read(ref _generation), userId));

    /// <summary>A new generation: every older entry is unreachable at once and leaves memory when its time is up.</summary>
    public void ForgetAll() => Interlocked.Increment(ref _generation);

    private async Task<AccessSnapshot?> LoadAsync(Key key)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IAccessSnapshotLoader>().LoadAsync(key.UserId, CancellationToken.None);
        }
        catch
        {
            cache.Remove(key);
            throw;
        }
    }
}
