namespace Ims.Application.Features.Reports;

/// <summary>
/// The receipt/issue detail: the posting row of every line of a posted document, plus a reversal row for every line of a reversed document, in the
/// one order of <see cref="MovementOrder"/>. Implemented in Infrastructure because the two kinds of row come from different predicates that a
/// specification over one query cannot merge. The page, its total and the export all come from here, so they always agree.
/// </summary>
public interface IMovementReader
{
    /// <remarks>Every method takes the filter's warehouse and product as the integer ids the handler translated from the public ids (see <c>IdMapExtensions</c>); null = no such filter.</remarks>
    Task<int> CountAsync(MovementFilter filter, int? warehouseId, int? productId, IReadOnlyCollection<int>? warehouseScope, CancellationToken ct);

    /// <summary>The page the filter asks for (<c>Page</c> and <c>PageSize</c>).</summary>
    Task<IReadOnlyList<MovementRow>> PageAsync(MovementFilter filter, int? warehouseId, int? productId, IReadOnlyCollection<int>? warehouseScope, CancellationToken ct);

    /// <summary>The first <paramref name="limit"/> rows in the same order, ignoring paging (exports).</summary>
    Task<IReadOnlyList<MovementRow>> ListAsync(MovementFilter filter, int? warehouseId, int? productId, IReadOnlyCollection<int>? warehouseScope, int limit, CancellationToken ct);
}
