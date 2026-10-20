namespace Ims.Domain;

/// <summary>
/// Shape rules of the tax-ready fields of the accounting module (HS code, SST code, TIN, SST registration number): which characters and how many, and nothing more. There is no
/// checksum and no lookup, because the system only carries the values to the accountant's package (PRD non-goal 2.2). The only normalisation is trimming the ends; a blank value
/// means "none" and is stored as <c>null</c>. Case is kept as typed. Characters are ASCII only: a Unicode digit or letter is refused.
/// One definition for the entity, the request validators and the import, so the three cannot disagree. The web mirrors the characters and lengths in TAX_FIELD; web/libs/shared/utils/src/lib/forms/tax-field.validator.spec.ts string-matches the flag lists of the four <c>*Error</c> methods and the <c>Clean</c> line below, so renaming or reformatting them fails that spec on purpose. No message repeats the value (a TIN can be a person's).
/// </summary>
public static class TaxFieldShapes
{
    /// <summary>The value as it is stored: trimmed, or <c>null</c> when blank.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The error for a trimmed value, or <c>null</c> when it is fine (or absent).</summary>
    public static string? HsCodeError(string? clean) => Error(clean, Limits.HsCodeMaxLength, digits: true, letters: false, dot: true, hyphen: false, Messages.HsCodeInvalid);

    public static string? SstCodeError(string? clean) => Error(clean, Limits.SstCodeMaxLength, digits: true, letters: true, dot: false, hyphen: true, Messages.SstCodeInvalid);

    public static string? TinError(string? clean) => Error(clean, Limits.TinMaxLength, digits: true, letters: true, dot: false, hyphen: false, Messages.TinInvalid);

    public static string? SstRegNoError(string? clean) => Error(clean, Limits.SstRegNoMaxLength, digits: true, letters: true, dot: false, hyphen: true, Messages.SstRegNoInvalid);

    /// <summary>Trims and checks; throws a <see cref="DomainException"/> with the field's message when the shape is wrong.</summary>
    internal static string? Accept(string? value, Func<string?, string?> error)
    {
        var clean = Clean(value);
        if (error(clean) is { } message) throw new DomainException(message);
        return clean;
    }

    private static string? Error(string? clean, int max, bool digits, bool letters, bool dot, bool hyphen, string message)
    {
        if (clean is null) return null;
        if (clean.Length > max) return message;
        foreach (var c in clean)
        {
            var allowed = (digits && c is >= '0' and <= '9') || (letters && c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z')) || (dot && c == '.') || (hyphen && c == '-');
            if (!allowed) return message;
        }
        return null;
    }
}
