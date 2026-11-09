namespace Ims.Application.Features.Sales;

/// <summary>Which list a looked-up price came from.</summary>
public enum PriceSource
{
    /// <summary>The customer's own list had an item for the product and unit.</summary>
    Customer,

    /// <summary>The customer's list had none (or the customer has no list) and the default list did.</summary>
    Default
}
