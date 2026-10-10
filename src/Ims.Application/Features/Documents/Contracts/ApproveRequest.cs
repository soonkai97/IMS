namespace Ims.Application.Features.Documents;

/// <param name="NegativeStockOverrideReason">Required when the document <c>requiresOverride</c>: why the approver lets stock go below zero. Ignored for any other document.</param>
public record ApproveRequest(string? NegativeStockOverrideReason);
