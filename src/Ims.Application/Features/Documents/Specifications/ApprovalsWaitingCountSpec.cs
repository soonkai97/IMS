using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Submitted documents held for approval, narrowed to the tiers <c>actor</c> may review (<see cref="ApprovalPolicy.MayReview(ActorRights, bool)"/>) and, when self-approval is off, without the caller's own submissions.</summary>
public sealed class ApprovalsWaitingCountSpec : Specification<StockDocument>
{
    public ApprovalsWaitingCountSpec(ActorRights actor, bool allowSelfApproval, IReadOnlyCollection<int>? scope)
    {
        Query.AsNoTracking().Where(d => d.Status == DocumentStatus.Submitted && d.RequiresApproval);
        if (scope is not null) Query.Where(d => scope.Contains(d.WarehouseId));
        if (!allowSelfApproval) Query.Where(d => d.SubmittedBy != actor.UserId);
        // A large document goes to the holders of the large tier, any other to the holders of the standard tier; whoever holds both sees every one.
        var standard = actor.Has(Permissions.DocumentApprove);
        var large = actor.Has(Permissions.DocumentApproveLarge);
        if (standard && large) return;
        if (large) Query.Where(d => d.IsLarge);
        else if (standard) Query.Where(d => !d.IsLarge);
        else Query.Where(d => false);
    }
}
