using Ardalis.Specification;
using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public sealed class UomCodeTakenSpec : Specification<Uom>
{
    public UomCodeTakenSpec(string code, int exceptId) => Query.Where(u => u.Code == code && u.Id != exceptId).AsNoTracking();
}
