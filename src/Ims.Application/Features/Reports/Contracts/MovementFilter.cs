using Ims.Domain;
namespace Ims.Application.Features.Reports;

/// <summary>Paging applies to the on-screen list; exports ignore it and are capped by <see cref="Limits.MaxExportRows"/>.</summary>
public record MovementFilter(DocumentType? Type, Guid? WarehouseId, int? ProductId, DateOnly? From, DateOnly? To,
    int Page = 1, int PageSize = Limits.DefaultListPageSize);
