using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Tracked document with its lines, for any command that changes it.</summary>
public sealed class DocumentForUpdateSpec : Specification<StockDocument>
{
    public DocumentForUpdateSpec(Guid publicId) => Query.Where(d => d.PublicId == publicId).Include(d => d.Lines);
}
