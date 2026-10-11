namespace Ims.Domain;

/// <summary>
/// What one receipt did to the moving average of its product: the average just before it was posted and the one it produced.
/// Kept on the posted receipt line so a reversal can put the average back exactly (see <see cref="StockBalance.Unreceive"/>).
/// </summary>
public readonly record struct AverageCostChange(decimal Before, decimal After);
