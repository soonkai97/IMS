using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Submitted documents held for approval, narrowed to what <c>role</c> could approve and, when self-approval is off, without the caller's own submissions.</summary>
public sealed class ApprovalsWaitingCountSpec : Specification<StockDocument>
{
    public ApprovalsWaitingCountSpec(string role, int userId, bool allowSelfApproval, IReadOnlyCollection<int>? scope)
    {
        Query.AsNoTracking().Where(d => d.Status == DocumentStatus.Submitted && d.RequiresApproval);
        if (scope is not null) Query.Where(d => scope.Contains(d.WarehouseId));
        if (!allowSelfApproval) Query.Where(d => d.SubmittedBy != userId);
        // Large documents are the Owner's; a supervisor handles the rest; the Admin may approve anything.
        if (role == Roles.Owner) Query.Where(d => d.IsLarge);
        else if (role == Roles.Supervisor) Query.Where(d => !d.IsLarge);
    }
}
