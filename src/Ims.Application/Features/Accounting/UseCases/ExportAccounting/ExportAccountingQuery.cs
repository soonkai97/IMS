using Ims.Application.Features.Reports;
namespace Ims.Application.Features.Accounting;

public record ExportAccountingQuery(AccountingExportFilter Filter) : IQuery<ExportFile>;
