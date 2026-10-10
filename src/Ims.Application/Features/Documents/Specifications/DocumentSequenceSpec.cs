using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed class DocumentSequenceSpec : Specification<DocumentSequence>
{
    public DocumentSequenceSpec(string prefix, int yearMonth, int warehouseId) =>
        Query.Where(s => s.Prefix == prefix && s.YearMonth == yearMonth && s.WarehouseId == warehouseId);
}
