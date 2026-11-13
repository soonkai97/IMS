using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The answer to a price lookup. Nothing found is an ordinary answer: <c>found</c> false, no price, no source (the person types the price).</summary>
/// <param name="Found">True when a live list has an item for the product and unit.</param>
/// <param name="Price">The selling price per ONE of the unit, in money. Absent for a caller without <c>so.manage</c> (every caller of this endpoint holds it, a price-list editor's permission is not what opens it).</param>
/// <param name="Source">The customer's own list, or the default list; null when nothing was found.</param>
public sealed record PriceLookupDto(bool Found, [property: VisibleWith(Permissions.SoManage), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Price, PriceSource? Source)
{
    public static PriceLookupDto None { get; } = new(false, null, null);
}
