using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Partners;

/// <summary>A live partner of the same type already has this name. The comparison follows the database collation (case-insensitive), like the unique index.</summary>
public sealed class PartnerNameTakenSpec : Specification<Partner>
{
    public PartnerNameTakenSpec(PartnerType type, string name, int exceptId) =>
        Query.Where(p => p.Type == type && p.Name == name && p.Id != exceptId).AsNoTracking();
}
