namespace Ims.Application;

/// <summary>The row version of a document.</summary>
public sealed record DocumentStamp(byte[] RowVersion);
