using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>The Posted document a return is saved against, loaded with its lines, products and units: what the line builder needs to turn the lines of a request into lines of the return.</summary>
public sealed record ReturnLinkedOriginal(StockDocument Original) : LinkedOrder;
