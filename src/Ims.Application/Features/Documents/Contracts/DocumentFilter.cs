using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <param name="Mine">True returns only documents the caller created.</param>
/// <param name="RequiresApproval">True returns only documents an approval rule held back; false only those it did not. Combine with <c>status=Submitted</c> for the approvals inbox.</param>
/// <param name="Cursor">Seek paging: the <c>nextCursor</c> of the previous page, unchanged; returns the documents after it (constant cost at any depth).</param>
/// <param name="IncludeTotal">False skips the row count.</param>
/// <param name="Search">Documents whose number or remark contains the text, or whose partner name contains it (case-insensitive; a partial number such as 0100 works).</param>
public record DocumentFilter(DocumentType? Type, DocumentStatus? Status, int? WarehouseId, DateOnly? From, DateOnly? To,
    string? Search, int Page = 1, int PageSize = Limits.DefaultPageSize, string? Cursor = null, bool IncludeTotal = true,
    bool? Mine = null, bool? RequiresApproval = null);
