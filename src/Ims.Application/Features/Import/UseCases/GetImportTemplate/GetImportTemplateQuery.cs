using Ims.Application.Features.Reports;
namespace Ims.Application.Features.Import;

public record GetImportTemplateQuery(string Kind) : IQuery<ExportFile>;
