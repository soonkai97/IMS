namespace Ims.Application.Features.Documents;

/// <summary>What a person recognises a product by in a sentence: its SKU and the unit its stock is counted in. <c>PublicId</c> is the id a client may see.</summary>
public record ProductLabel(int Id, Guid PublicId, string Sku, string UomCode);
