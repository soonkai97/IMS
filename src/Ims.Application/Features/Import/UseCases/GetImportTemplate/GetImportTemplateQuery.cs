using Ims.Application.Features.Reports;
namespace Ims.Application.Features.Import;

public sealed record GetImportTemplateQuery(string Kind) : IQuery<ExportFile>;
