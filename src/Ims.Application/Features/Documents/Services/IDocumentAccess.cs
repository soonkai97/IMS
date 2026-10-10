using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Who may see and do what. Warehouse data scope and the action list the UI renders.</summary>
public interface IDocumentAccess
{
    void EnsureWarehouse(int warehouseId);
    /// <summary>Null = the caller sees every warehouse.</summary>
    IReadOnlyCollection<int>? WarehouseScope();
    IReadOnlyList<string> AllowedActions(DocumentDto document, SystemSettings settings);
}
