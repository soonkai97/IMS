namespace Ims.Domain;

/// <summary>
/// Which way a posted document moves stock: +1 adds (receipt), -1 removes (issue). Reversing a document flips it. This is the single statement of
/// the sign convention behind the <c>stockEffect</c> column of the movements report (the report's query repeats it as an expression because a
/// database query cannot call this method; a test keeps the two equal). A transfer will need a direction per line (out of one warehouse, into
/// another) when it becomes a document type.
/// </summary>
public static class StockDirection
{
    public const int Adds = 1;
    public const int Removes = -1;

    public static int Of(DocumentType type) => type switch
    {
        DocumentType.Receipt => Adds,
        DocumentType.Issue => Removes,
        _ => throw new DomainException(Messages.PostingNotSupported(type))
    };

    /// <summary>The signed effect on stock, in base units, of posting (or, with <paramref name="reversal"/>, reversing) a line.</summary>
    public static decimal EffectOf(DocumentType type, decimal baseQty, bool reversal = false) =>
        Of(type) * (reversal ? -1 : 1) * baseQty;
}
