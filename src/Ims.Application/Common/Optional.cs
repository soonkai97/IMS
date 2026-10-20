using System.Text.Json.Serialization;
namespace Ims.Application;

/// <summary>
/// A request field that tells "the key was not sent" from "the key was sent as null". <c>default</c> is absent, so a client that does not know the field (an old web bundle, a script)
/// leaves it out and the handler keeps the stored value; an explicit <c>null</c> (or a blank) is a present value and clears it. Only for fields where a client may legitimately omit the key
/// and the two cases must differ (the tax-ready fields of the accounting module); every other field stays a plain nullable property. A plain value converts implicitly and is present, even when it is null.
/// On the wire it is the bare value (<see cref="OptionalJsonConverter"/>) and in OpenAPI it is an optional nullable property (<c>OptionalSchemaTransformer</c>).
/// </summary>
[JsonConverter(typeof(OptionalJsonConverter))]
public readonly record struct Optional<T>
{
    public Optional(T? value)
    {
        Value = value;
        IsPresent = true;
    }

    /// <summary>The value that was sent, or <c>default</c> (null for text) when the key was absent: a rule that checks the shape of a value passes an absent field without a special case.</summary>
    public T? Value { get; }

    /// <summary>True when the key was in the request, even with a null.</summary>
    public bool IsPresent { get; }

    /// <summary>The value that was sent, or <paramref name="stored"/> when the key was absent.</summary>
    public T? Or(T? stored) => IsPresent ? Value : stored;

    public static implicit operator Optional<T>(T? value) => new(value);
}
