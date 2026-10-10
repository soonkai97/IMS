using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>Tracked document with its lines, for any command that changes it.</summary>
public sealed class DocumentForUpdateSpec : Specification<StockDocument>
{
    public DocumentForUpdateSpec(int id) => Query.Where(d => d.Id == id).Include(d => d.Lines);
}
