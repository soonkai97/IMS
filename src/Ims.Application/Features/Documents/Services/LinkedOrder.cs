namespace Ims.Application.Features.Documents;

/// <summary>The order a linked draft is saved against, loaded with its lines, products and units: what the line builder needs to turn the lines of a request into lines of the document.</summary>
public abstract record LinkedOrder;
