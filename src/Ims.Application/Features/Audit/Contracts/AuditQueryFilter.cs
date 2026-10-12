using Ims.Domain;
namespace Ims.Application.Features.Audit;

/// <summary>
/// Every text filter is index-friendly (an exact match, a prefix, or a small resolved list), never a scan of the table.
/// </summary>
/// <param name="EntityType">A fragment of the entity name, case-insensitive: "doc" finds StockDocument, "product" finds Product and ProductBarcode (see <c>GET /api/audit/filters</c>).</param>
/// <param name="Actor">User name starts with (case-insensitive): "staf" finds staff.</param>
/// <param name="Cursor">Seek paging: the <c>nextCursor</c> of the previous page, unchanged; returns the entries after it.</param>
/// <param name="IncludeTotal">False skips the row count.</param>
/// <param name="EntityId">Id of the entity starts with: a document number such as ISS-WH01-2610-0017, a user id, a SKU prefix of the bulk events.</param>
/// <param name="Action">Action starts with: "document." for every document event, "login" for sign-ins, "document.posted" for one.</param>
/// <param name="HideSignIns">Leave out the sign-in events (login.success, login.failed, login.locked, login.lockout, logout), which are most of a busy log: "everything except sign-ins". Combines with the other filters.</param>
/// <param name="Search">One box for a person: matches an entity name fragment OR an entity id prefix, so a document number and "Document" both work.</param>
public record AuditQueryFilter(string? EntityType, string? Actor, DateOnly? From, DateOnly? To, int Page = 1,
    int PageSize = Limits.DefaultListPageSize, string? Cursor = null, bool IncludeTotal = true,
    string? EntityId = null, string? Action = null, string? Search = null, bool HideSignIns = false);
