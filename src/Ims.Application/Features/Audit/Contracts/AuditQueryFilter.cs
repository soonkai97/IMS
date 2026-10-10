using Ims.Domain;
namespace Ims.Application.Features.Audit;

/// <summary>
/// Every text filter is index-friendly (an exact match, a prefix, or a small resolved list), never a scan of the table.
/// </summary>
/// <param name="EntityType">A fragment of the entity name, case-insensitive: "doc" finds StockDocument, "product" finds Product and ProductBarcode (see <c>GET /api/audit/filters</c>).</param>
/// <param name="Actor">User name starts with (case-insensitive): "staf" finds staff.</param>
/// <param name="AfterId">Seek paging: return entries older than this id.</param>
/// <param name="IncludeTotal">False skips the row count.</param>
/// <param name="EntityId">Id of the entity starts with: a document number such as ISS-WH01-2610-0017, a user id, a SKU prefix of the bulk events.</param>
/// <param name="Action">Action starts with: "document." for every document event, "login" for sign-ins, "document.posted" for one.</param>
/// <param name="Search">One box for a person: matches an entity name fragment OR an entity id prefix, so a document number and "Document" both work.</param>
public record AuditQueryFilter(string? EntityType, string? Actor, DateOnly? From, DateOnly? To, int Page = 1,
    int PageSize = Limits.DefaultListPageSize, long? AfterId = null, bool IncludeTotal = true,
    string? EntityId = null, string? Action = null, string? Search = null);
