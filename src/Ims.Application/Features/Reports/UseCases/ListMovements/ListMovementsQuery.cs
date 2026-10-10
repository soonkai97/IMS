namespace Ims.Application.Features.Reports;

public record ListMovementsQuery(MovementFilter Filter) : IQuery<PagedResult<MovementRow>>;
