namespace Ims.Application.Features.Catalog;

/// <summary>Rules and persistence shared by create and update, so each handler stays small (SRP).</summary>
public interface IProductWriter
{
    /// <summary>Creates (id null) or updates the product with that public id (<c>NotFoundException</c> when there is none) and returns its integer key.</summary>
    Task<int> SaveAsync(Guid? id, ProductSaveRequest request, CancellationToken ct);
}
