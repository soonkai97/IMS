namespace Ims.Application.Features.Sales;

/// <summary>A whole price list: every field is sent on every save (there is no "absent = unchanged"), and the items replace the old ones.</summary>
/// <param name="Name">Unique among the price lists (case-insensitive), at most 200 characters.</param>
/// <param name="IsDefault">True for the one list that prices every customer without a list of their own. Only one list can be the default (409 otherwise), and it has no customer.</param>
/// <param name="CustomerId">An active partner of type Customer, who then has this list; a customer has at most one (409 otherwise). Null for the default list and for a list not yet assigned.</param>
public record PriceListSaveRequest(string Name, bool IsDefault, Guid? CustomerId, IReadOnlyList<PriceListItemInput> Items);
