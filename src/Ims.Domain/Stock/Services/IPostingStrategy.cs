namespace Ims.Domain;

/// <summary>
/// How one document type moves stock. Open/closed: Transfer and Adjust (Phase 2) add a strategy and register it;
/// the engine and the use cases do not change. Strategies are pure: no I/O, so they are unit-testable.
/// </summary>
public interface IPostingStrategy
{
    DocumentType Type { get; }
    void Post(StockDocumentLine line, StockBalance balance, PostingContext ctx);
    void Reverse(StockDocumentLine line, StockBalance balance, PostingContext ctx);
}
