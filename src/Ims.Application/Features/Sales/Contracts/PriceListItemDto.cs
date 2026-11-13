using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <param name="UomCode">The code of the unit the price is for.</param>
/// <param name="Price">The selling price per ONE of the unit, in money. Absent for a caller without <c>pricelist.manage</c>; every price-list endpoint needs it, so the field is always present there.</param>
public sealed record PriceListItemDto(Guid ProductId, string Sku, string ProductName, Guid UomId, string UomCode,
    [property: VisibleWith(Permissions.PricelistManage), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Price);
