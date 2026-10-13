namespace Ims.Domain;

/// <summary>A product a reversal would push below zero, named by its public id (it goes to the client as is). <paramref name="Balance"/> is the resulting (negative) quantity in the product's base unit.</summary>
public record ReversalShortage(Guid ProductId, string Sku, string UomCode, decimal Balance);
