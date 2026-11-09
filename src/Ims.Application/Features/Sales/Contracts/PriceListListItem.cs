using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace Ims.Application.Features.Sales;

/// <param name="ItemCount">How many items the list holds.</param>
/// <param name="RowVersion">Source of <see cref="ConcurrencyToken"/>; not part of the JSON.</param>
public record PriceListListItem(Guid Id, string Name, bool IsDefault, Guid? CustomerId, string? CustomerName, int ItemCount, [property: JsonIgnore] byte[] RowVersion)
{
    /// <summary>The token of the list, as in <see cref="PriceListDto.ConcurrencyToken"/>: send it as If-Match to delete the list without opening it.</summary>
    [Required]
    public string ConcurrencyToken { get; init; } = "";
}
