namespace Ims.Application.Features.Documents;

/// <summary>What a person recognises a product by in a sentence: its SKU and the unit its stock is counted in.</summary>
public record ProductLabel(int Id, string Sku, string UomCode);
