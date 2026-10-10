using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <param name="Mine">True returns only documents the caller created.</param>
/// <param name="RequiresApproval">True returns only documents an approval rule held back; false only those it did not. Combine with <c>status=Submitted</c> for the approvals inbox.</param>
/// <param name="AfterId">Seek paging: return documents older than this id (constant cost at any depth).</param>
/// <param name="IncludeTotal">False skips the row count.</param>
public record DocumentFilter(DocumentType? Type, DocumentStatus? Status, int? WarehouseId, DateOnly? From, DateOnly? To,
    string? Search, int Page = 1, int PageSize = Limits.DefaultPageSize, int? AfterId = null, bool IncludeTotal = true,
    bool? Mine = null, bool? RequiresApproval = null);
