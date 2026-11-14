namespace Ims.Domain;

/// <summary>
/// Marks a value object: a concept with rules and no identity, compared by value (money, a quantity with its unit, a document number). The interface has no members on purpose; it exists so that
/// <c>ValueObjectDesignTests</c> can find every value object and hold it to one design: a struct or a sealed record, validated when it is created, with no setter (not even <c>init</c>), no mutable field,
/// no public mutating <c>void</c> method and no persistence or serialization attribute. The database mapping belongs to Infrastructure (a value converter), the wire shape to Application.
/// </summary>
public interface IValueObject;
