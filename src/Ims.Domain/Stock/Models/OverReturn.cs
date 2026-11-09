namespace Ims.Domain;

/// <summary>A return asks for more of an original line than remains. All figures are in the original line's unit.</summary>
public readonly record struct OverReturn(int LineNo, decimal Original, decimal Returned, decimal Remaining, decimal Requested);
