using Ims.Application.Features.Sales;

namespace Ims.Api.Tests.Builders;

/// <summary>Builds a price-list save request from simple items.</summary>
public sealed class PriceListBuilder(string name)
{
    private readonly List<PriceListItemInput> _items = [];
    private bool _isDefault;
    private Guid? _customerId;

    public PriceListBuilder Default() { _isDefault = true; return this; }
    public PriceListBuilder ForCustomer(Guid customerId) { _customerId = customerId; return this; }

    public PriceListBuilder WithItem(Guid productId, decimal price, Guid? uomId = null)
    {
        _items.Add(new PriceListItemInput(productId, uomId, price));
        return this;
    }

    public PriceListSaveRequest Build() => new(name, _isDefault, _customerId, _items);
}
