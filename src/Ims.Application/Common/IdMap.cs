using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// <see cref="IIdMap{T}"/> over the read repository, registered scoped. Each direction remembers what it found: the answers are immutable (an id never changes), and the
/// two directions are kept apart because they treat a soft-deleted row differently, so one must not fill the other.
/// </summary>
public sealed class IdMap<T>(IReadRepository<T> repository) : IIdMap<T> where T : PublicEntity
{
    private readonly Dictionary<Guid, int> _internal = [];
    private readonly Dictionary<int, Guid> _public = [];

    public async Task<IReadOnlyDictionary<Guid, int>> ToInternalAsync(IEnumerable<Guid> publicIds, CancellationToken ct = default)
    {
        var wanted = publicIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var missing = wanted.Where(id => !_internal.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            foreach (var pair in await repository.ListAsync(IdPairsSpec<T>.ForPublicIds(missing), ct))
                _internal[pair.PublicId] = pair.Id;

        return wanted.Where(_internal.ContainsKey).ToDictionary(id => id, id => _internal[id]);
    }

    public async Task<IReadOnlyDictionary<int, Guid>> ToPublicAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var wanted = ids.Distinct().ToList();
        var missing = wanted.Where(id => !_public.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            foreach (var pair in await repository.ListAsync(IdPairsSpec<T>.ForIds(missing), ct))
                _public[pair.Id] = pair.PublicId;

        return wanted.Where(_public.ContainsKey).ToDictionary(id => id, id => _public[id]);
    }
}
