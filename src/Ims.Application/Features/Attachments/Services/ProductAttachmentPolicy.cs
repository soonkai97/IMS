using Ims.Domain;
namespace Ims.Application.Features.Attachments;

/// <summary>Product images and datasheets: read with master-data read, add and remove with master-data manage.</summary>
public class ProductAttachmentPolicy(IReadRepository<Product> products, IIdMap<Product> productIds, ICurrentUser current) : IAttachmentOwnerPolicy, IAppService
{
    public AttachmentOwnerType OwnerType => AttachmentOwnerType.Product;

    public async Task<int> ResolveAsync(Guid ownerPublicId, CancellationToken ct) =>
        await productIds.ToInternalOrNoRowAsync(ownerPublicId, ct) ?? IdMapExtensions.NoRow;

    public async Task EnsureCanReadAsync(int ownerId, CancellationToken ct)
    {
        Require(Permissions.MasterDataRead);
        await EnsureExistsAsync(ownerId, ct);
    }

    public async Task EnsureCanWriteAsync(int ownerId, CancellationToken ct)
    {
        Require(Permissions.MasterDataManage);
        await EnsureExistsAsync(ownerId, ct);
    }

    public Task EnsureCanDeleteAsync(int ownerId, CancellationToken ct) => EnsureCanWriteAsync(ownerId, ct);

    private void Require(string permission)
    {
        if (!Permissions.Has(current.Role, permission)) throw new ForbiddenException(Messages.NotAllowed);
    }

    private async Task EnsureExistsAsync(int productId, CancellationToken ct)
    {
        if (!await products.AnyAsync(new ByIdSpec<Product>(productId, tracked: false), ct)) throw new NotFoundException(Messages.ProductNotFound);
    }
}
