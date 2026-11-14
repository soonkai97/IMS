namespace Ims.Domain;

/// <summary>
/// The reason a person gives for rejecting, cancelling, reversing, closing short, overriding or switching a module (BR-02, OUT-02): kept trimmed, and at least <see cref="Limits.ReasonMinLength"/> characters once
/// trimmed, so "x" or spaces cannot satisfy the audit trail. It is a value: two reasons with the same text are equal, and one is built only through <see cref="Required"/> or <see cref="Of"/>, which refuse a text
/// that breaks the rule with a <see cref="DomainException"/>. The aggregate keeps the text (<see cref="Text"/>) in the same column as before; the value stays in the Domain and Application.
/// <para>
/// The sentence for "none given" belongs to the action ("A rejection reason is required."), so the caller passes it and each call site keeps its own message. The sentence for "too short" is the shared
/// <see cref="Messages.ReasonTooShort"/>. The order is fixed: nothing given is answered first, then too short.
/// </para>
/// </summary>
public readonly record struct Reason : IValueObject
{
    private readonly string? _text;

    private Reason(string text) => _text = text;

    /// <summary>The reason without the spaces around it. The empty text only for a <c>default</c> value, which no factory returns.</summary>
    public string Text => _text ?? string.Empty;

    public override string ToString() => Text;

    /// <summary>
    /// A reason that must be given: <paramref name="whenMissing"/> when there is none (null, empty or spaces), <see cref="Messages.ReasonTooShort"/> when it is shorter than
    /// <see cref="Limits.ReasonMinLength"/> once trimmed.
    /// </summary>
    public static Reason Required(string? text, string whenMissing)
    {
        BusinessRules.Ensure(new ReasonRequiredRule(text, whenMissing));
        BusinessRules.Ensure(new ReasonRequiredRule(text, Messages.ReasonTooShort, Limits.ReasonMinLength));
        return new Reason(text!.Trim());
    }

    /// <summary>A reason for an action that has one sentence for both refusals: <paramref name="refusal"/> when there is none and when it is shorter than <see cref="Limits.ReasonMinLength"/> once trimmed.</summary>
    public static Reason Of(string? text, string refusal)
    {
        BusinessRules.Ensure(new ReasonRequiredRule(text, refusal, Limits.ReasonMinLength));
        return new Reason(text!.Trim());
    }
}
