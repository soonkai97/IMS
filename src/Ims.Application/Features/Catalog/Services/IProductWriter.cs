namespace Ims.Application.Features.Catalog;

/// <summary>Rules and persistence shared by create and update, so each handler stays small (SRP).</summary>
public interface IProductWriter
{
    /// <summary>Creates (id null) or updates a product and returns its id.</summary>
    Task<int> SaveAsync(int? id, ProductSaveRequest request, CancellationToken ct);
}
