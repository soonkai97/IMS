namespace Ims.Domain;

/// <summary>One line of a return as the original sees it: which original line (its position), and how much of it, in the original line's unit.</summary>
public readonly record struct ReturnQuantity(int LineNo, decimal Qty);
