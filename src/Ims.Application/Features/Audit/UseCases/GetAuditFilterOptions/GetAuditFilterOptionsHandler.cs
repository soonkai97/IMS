using Ims.Domain;
namespace Ims.Application.Features.Audit;

public sealed class GetAuditFilterOptionsHandler : IQueryHandler<GetAuditFilterOptionsQuery, AuditFilterOptions>
{
    public Task<AuditFilterOptions> HandleAsync(GetAuditFilterOptionsQuery q, CancellationToken ct) =>
        Task.FromResult(new AuditFilterOptions(AuditEntities.Known, AuditActions.Known));
}
