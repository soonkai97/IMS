using Ims.Application.Features.Stock;
namespace Ims.Application.Features.Reports;

public record ExportStockSummaryQuery(StockFilter Filter) : IQuery<ExportFile>;
