namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>One line as a person enters it: the product, the quantity in <paramref name="UomId"/> (null = the base unit) and, on a receipt, the cost per entered unit.</summary>
internal sealed record OracleLine(int ProductId, decimal Qty, decimal? Cost = null, int? UomId = null);
