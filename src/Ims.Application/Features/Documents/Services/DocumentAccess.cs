using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Warehouse data scope for the current caller. What they may do with a document is decided by <see cref="DocumentActionPolicy"/>.</summary>
public class DocumentAccess(ICurrentUser current) : IDocumentAccess, IAppService
{
    public void EnsureWarehouse(int warehouseId)
    {
        if (!current.CanAccessWarehouse(warehouseId))
            throw new ForbiddenException(Messages.NoWarehouseAccess);
    }

    public IReadOnlyCollection<int>? WarehouseScope() => Roles.SeesAllWarehouses(current.Role) ? null : current.WarehouseIds;

    public IReadOnlyList<string> AllowedActions(DocumentDto d, SystemSettings settings) =>
        DocumentActionPolicy.AllowedActions(current.Role, current.UserId, d.Status, d.RequiresApproval, d.SubmittedById, settings.AllowSelfApproval);
}
