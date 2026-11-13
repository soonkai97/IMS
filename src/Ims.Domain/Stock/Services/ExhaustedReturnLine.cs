namespace Ims.Domain;

/// <summary>A return line that cannot be returned because the base quantity of the original line it names is used up: the position of that original line and the return line itself.</summary>
public sealed record ExhaustedReturnLine(int LineNo, StockDocumentLine Line);
