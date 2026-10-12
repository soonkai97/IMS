using Ims.Domain;
namespace Ims.Application;

public static class IdMapExtensions
{
    /// <summary>Identity keys start at 1, so this integer id names no row: a filter or a reference built on it matches nothing, like any id that does not exist.</summary>
    public const int NoRow = 0;

    /// <summary>
    /// The integer id for ONE public id a client sent as a filter or a reference. Null stays null (no filter). An id that names no row (unknown, empty, soft-deleted) becomes
    /// <see cref="NoRow"/>, so the query returns nothing exactly as a filter on an integer id that does not exist did; the client learns nothing about other rows.
    /// </summary>
    public static async Task<int?> ToInternalOrNoRowAsync<T>(this IIdMap<T> map, Guid? publicId, CancellationToken ct = default) where T : PublicEntity
    {
        if (publicId is not { } id) return null;
        return (await map.ToInternalAsync([id], ct)).GetValueOrDefault(id, NoRow);
    }
}
