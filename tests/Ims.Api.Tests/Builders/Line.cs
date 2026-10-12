namespace Ims.Api.Tests.Builders;

/// <summary>One document line as a test thinks of it. <paramref name="UnitCost"/> is per entered unit and only matters for receipts.</summary>
public readonly record struct Line(Guid ProductId, decimal Qty, decimal? UnitCost = null, Guid? UomId = null);
