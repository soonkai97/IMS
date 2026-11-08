namespace Ims.Application.Features.Sales;

/// <summary>The row version of a document.</summary>
public sealed record DocumentStamp(byte[] RowVersion);
