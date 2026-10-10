namespace Ims.Application.Features.Reports;

public record ExportMovementsQuery(MovementFilter Filter) : IQuery<ExportFile>;
