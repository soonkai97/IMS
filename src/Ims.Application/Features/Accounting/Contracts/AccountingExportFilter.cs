namespace Ims.Application.Features.Accounting;

/// <summary>
/// Which posted lines go into the accounting file: the period (both dates are required, inclusive, Kuala Lumpur days) and, optionally, one warehouse. A warehouse the caller may not
/// read is the same 404 as one that does not exist. The dates are nullable only so a missing one is answered with a sentence instead of a binder error.
/// </summary>
public record AccountingExportFilter(DateOnly? From, DateOnly? To, Guid? WarehouseId);
