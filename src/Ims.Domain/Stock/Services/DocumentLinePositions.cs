namespace Ims.Domain;

/// <summary>
/// The one definition of the position of a document line: 1 to n over the document's lines ordered by their key. A document line has no number of its own; a return names the line it returns by this
/// position (<see cref="StockDocumentLine.ReturnOfLineNo"/>), and the read side lists the lines in the same order (<c>OrderBy(Id)</c> in the document query), so index plus one is the position. A line of a
/// Posted document never changes, so the position is stable. Every place that turns a position into a line or the other way goes through here.
/// </summary>
public static class DocumentLinePositions
{
    /// <summary>The lines with their positions, in position order.</summary>
    public static IReadOnlyList<(int LineNo, StockDocumentLine Line)> Of(IEnumerable<StockDocumentLine> lines) =>
        lines.OrderBy(l => l.Id).Select((line, index) => (index + 1, line)).ToList();

    /// <summary>The line at a position, or null when the document has no such line.</summary>
    public static StockDocumentLine? At(IEnumerable<StockDocumentLine> lines, int lineNo) =>
        lineNo < 1 ? null : lines.OrderBy(l => l.Id).Skip(lineNo - 1).FirstOrDefault();
}
