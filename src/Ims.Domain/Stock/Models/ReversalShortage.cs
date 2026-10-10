namespace Ims.Domain;

/// <summary>A product a reversal would push below zero. <paramref name="Balance"/> is the resulting (negative) quantity in the product's base unit.</summary>
public record ReversalShortage(int ProductId, string Sku, string UomCode, decimal Balance);
