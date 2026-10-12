using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// Translates between the integer ids the system works with and the public ids a client sees, for one kind of entity, a list at a time (one query however many ids,
/// never one per row). One instance per request, so an id looked up twice in a request costs one query. Ids that do not exist are simply absent from the answer.
/// </summary>
public interface IIdMap<T> where T : PublicEntity
{
    /// <summary>
    /// Public ids from a client to integer ids. A soft-deleted row is absent, like in any query. An empty id (<see cref="Guid.Empty"/>) is absent without asking the database.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> ToInternalAsync(IEnumerable<Guid> publicIds, CancellationToken ct = default);

    /// <summary>Integer ids of rows already read to their public ids. A soft-deleted row is present: a document still names the product it was posted for.</summary>
    Task<IReadOnlyDictionary<int, Guid>> ToPublicAsync(IEnumerable<int> ids, CancellationToken ct = default);
}
