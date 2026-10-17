using Ims.Application;
using Ims.Domain;
using Ims.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ims.Api.Tests.Tests.Access;

/// <summary>The access snapshot cache without a database: hits, misses, forgetting, expiry, one load for many parallel callers, and failing closed.</summary>
public class CachedAccessSnapshotsTests
{
    private static CachedAccessSnapshots Build(IAccessSnapshotLoader loader, int ttlSeconds = 60)
    {
        var provider = new ServiceCollection().AddScoped(_ => loader).BuildServiceProvider();
        return new CachedAccessSnapshots(new MemoryCache(new MemoryCacheOptions()), provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AccessOptions { SnapshotTtlSeconds = ttlSeconds }));
    }

    private static CountingLoader Clerks() => new((id, _) => CountingLoader.Snapshot(id));

    [Fact]
    public async Task The_second_request_of_a_user_is_served_from_memory()
    {
        var loader = Clerks();
        var cache = Build(loader);

        var first = await cache.GetAsync(7, default);
        var second = await cache.GetAsync(7, default);

        Assert.Same(first, second);
        Assert.Equal(1, loader.Calls);
        Assert.Equal(Roles.Clerk, first!.RoleCode);
    }

    [Fact]
    public async Task Users_are_cached_apart()
    {
        var loader = Clerks();
        var cache = Build(loader);

        var a = await cache.GetAsync(1, default);
        var b = await cache.GetAsync(2, default);

        Assert.Equal(1, a!.UserId);
        Assert.Equal(2, b!.UserId);
        Assert.Equal(2, loader.Calls);
    }

    [Fact]
    public async Task Forgetting_a_user_reads_that_user_again_and_nobody_else()
    {
        var loader = new CountingLoader((id, call) => CountingLoader.Snapshot(id, call == 1 && id == 1 ? Roles.Admin : Roles.Clerk));
        var cache = Build(loader);
        Assert.Equal(Roles.Admin, (await cache.GetAsync(1, default))!.RoleCode);
        await cache.GetAsync(2, default);

        cache.Forget(1);

        Assert.Equal(Roles.Clerk, (await cache.GetAsync(1, default))!.RoleCode); // the demotion applies at once
        await cache.GetAsync(2, default);
        Assert.Equal(3, loader.Calls); // user 2 was not read again
    }

    [Fact]
    public async Task Forgetting_everyone_reads_every_user_again()
    {
        var loader = Clerks();
        var cache = Build(loader);
        await cache.GetAsync(1, default);
        await cache.GetAsync(2, default);

        cache.ForgetAll();
        await cache.GetAsync(1, default);
        await cache.GetAsync(2, default);
        await cache.GetAsync(2, default);

        Assert.Equal(4, loader.Calls);
    }

    [Fact]
    public async Task A_snapshot_is_read_again_when_its_time_is_up()
    {
        var loader = Clerks();
        var cache = Build(loader, ttlSeconds: 1);
        await cache.GetAsync(1, default);

        await Task.Delay(TimeSpan.FromMilliseconds(1300)); // the time to live is absolute, so waiting is the only way to age it
        await cache.GetAsync(1, default);

        Assert.Equal(2, loader.Calls);
    }

    [Fact]
    public async Task A_missing_user_is_remembered_as_missing_until_forgotten()
    {
        var loader = new CountingLoader((_, _) => null);
        var cache = Build(loader);

        Assert.Null(await cache.GetAsync(9, default));
        Assert.Null(await cache.GetAsync(9, default));
        Assert.Equal(1, loader.Calls);

        cache.Forget(9);
        Assert.Null(await cache.GetAsync(9, default));
        Assert.Equal(2, loader.Calls);
    }

    [Fact]
    public async Task Many_parallel_requests_of_one_user_cause_one_read()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loader = new CountingLoader((id, _) => CountingLoader.Snapshot(id), gate.Task);
        var cache = Build(loader);

        var requests = Enumerable.Range(0, 50).Select(_ => Task.Run(() => cache.GetAsync(5, default))).ToList();
        await Task.Delay(100); // they are all waiting on the same read
        gate.SetResult();
        var results = await Task.WhenAll(requests);

        Assert.Equal(1, loader.Calls);
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public async Task Parallel_requests_of_several_users_read_each_user_once()
    {
        var loader = Clerks();
        var cache = Build(loader);

        await Task.WhenAll(Enumerable.Range(0, 500).Select(i => Task.Run(() => cache.GetAsync(i % 5, default))));

        Assert.Equal(5, loader.Calls);
    }

    [Fact]
    public async Task A_failed_read_is_thrown_to_the_caller_and_is_not_remembered()
    {
        var failing = true;
        var loader = new CountingLoader((id, _) => failing ? throw new InvalidOperationException("database down") : CountingLoader.Snapshot(id));
        var cache = Build(loader);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync(3, default));
        failing = false;
        var recovered = await cache.GetAsync(3, default); // not a cached failure, and never an allow in between

        Assert.Equal(3, recovered!.UserId);
        Assert.Equal(2, loader.Calls);
    }

    [Fact]
    public async Task A_read_that_started_before_a_change_cannot_put_its_old_answer_back()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = Build(new SlowThenFast(gate.Task, started));

        var slow = cache.GetAsync(1, default);
        await started.Task;
        cache.Forget(1); // the change committed while that read was running
        var afterChange = await cache.GetAsync(1, default);
        gate.SetResult();
        var late = await slow;

        Assert.Equal(Roles.Clerk, afterChange!.RoleCode);
        Assert.Equal(Roles.Admin, late!.RoleCode); // the slow caller itself is answered with what it read
        Assert.Equal(Roles.Clerk, (await cache.GetAsync(1, default))!.RoleCode); // and nobody after it is
    }

    [Fact]
    public async Task A_caller_that_gives_up_does_not_cancel_the_read_the_others_share()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loader = new CountingLoader((id, _) => CountingLoader.Snapshot(id), gate.Task);
        var cache = Build(loader);
        using var cancel = new CancellationTokenSource();

        var impatient = cache.GetAsync(1, cancel.Token);
        var patient = cache.GetAsync(1, default);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => impatient);
        gate.SetResult();

        Assert.Equal(1, (await patient)!.UserId);
        Assert.Equal(1, loader.Calls);
    }

    /// <summary>The first read is slow and sees the old role; every later read sees the new one.</summary>
    private sealed class SlowThenFast(Task gate, TaskCompletionSource started) : IAccessSnapshotLoader
    {
        private int _calls;

        public async Task<AccessSnapshot?> LoadAsync(int userId, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) > 1) return CountingLoader.Snapshot(userId, Roles.Clerk);
            started.SetResult();
            await gate;
            return CountingLoader.Snapshot(userId, Roles.Admin);
        }
    }
}
