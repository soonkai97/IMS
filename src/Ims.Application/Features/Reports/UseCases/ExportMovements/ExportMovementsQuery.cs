namespace Ims.Application.Features.Reports;

public sealed record ExportMovementsQuery(MovementFilter Filter) : IQuery<ExportFile>;
