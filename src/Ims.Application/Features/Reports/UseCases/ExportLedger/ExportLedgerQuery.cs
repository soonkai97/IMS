using Ims.Application.Features.Stock;
namespace Ims.Application.Features.Reports;

public sealed record ExportLedgerQuery(LedgerFilter Filter) : IQuery<ExportFile>;
