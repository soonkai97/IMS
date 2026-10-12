namespace Ims.Domain;

/// <summary>
/// What one receipt did to the moving average of its product, kept on the posted receipt line so a reversal can put the average back
/// (see <see cref="StockBalance.Unreceive"/>): the average just before it posted, the one it produced, and the line that was the balance's latest stock-in
/// (a receipt line, or the line of a reversed issue) just before it.
/// </summary>
public readonly record struct AverageCostChange(decimal Before, decimal After, int? PreviousLineId);
