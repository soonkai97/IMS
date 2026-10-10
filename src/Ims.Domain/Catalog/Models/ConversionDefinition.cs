namespace Ims.Domain;

/// <summary>Value object describing one unit conversion before it is attached to a product.</summary>
public readonly record struct ConversionDefinition(int FromUomId, int ToUomId, decimal Factor);
