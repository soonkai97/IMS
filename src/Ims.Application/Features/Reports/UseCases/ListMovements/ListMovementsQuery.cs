namespace Ims.Application.Features.Reports;

public sealed record ListMovementsQuery(MovementFilter Filter) : IQuery<PagedResult<MovementRow>>;
