using Ims.Application.Features.Stock;
namespace Ims.Application.Features.Reports;

public sealed record ExportStockSummaryQuery(StockFilter Filter) : IQuery<ExportFile>;
