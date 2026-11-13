using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace Ims.Application.Features.Sales;

/// <param name="CustomerId">The customer this list is for; null for the default list and for a list not yet assigned.</param>
/// <param name="Items">Ordered by product name, then unit.</param>
/// <param name="RowVersion">Source of <see cref="ConcurrencyToken"/>; not part of the JSON.</param>
public sealed record PriceListDto(Guid Id, string Name, bool IsDefault, Guid? CustomerId, string? CustomerName, IReadOnlyList<PriceListItemDto> Items, [property: JsonIgnore] byte[] RowVersion)
{
    /// <summary>Changes whenever the list (name, default, customer or items) changes; opaque: compare it only for equality. Also sent as the ETag header; send it back as If-Match to refuse a stale save or delete.</summary>
    [Required]
    public string ConcurrencyToken { get; init; } = "";
}
