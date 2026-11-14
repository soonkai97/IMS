using System.Globalization;

namespace Ims.Domain;

/// <summary>
/// A document number (BR-11): <c>prefix-warehouse-yyMM-nnnn</c>, for example <c>RCV-WH1-2601-0001</c>. It is a value (compared by its text, ordinally), and its static members are the format rules every
/// caller already used: <see cref="PrefixFor"/>, <see cref="YearMonthOf"/> and <see cref="Format"/>.
/// <see cref="Create"/> builds a new number from its parts and refuses one the column cannot hold; <see cref="FromStored"/> is lenient and never throws, because a stored value must always load.
/// The parts are read from both ends (the prefix up to the first separator, the yyMM and the running number from the last two), since a warehouse code may itself contain a separator.
/// <c>default</c> is <see cref="Unassigned"/>: a draft that has no number yet (stored as the empty text).
/// </summary>
public readonly record struct DocumentNumber : IValueObject
{
    private readonly string? _text;

    private DocumentNumber(string text) => _text = text;

    /// <summary>No number yet; the empty text.</summary>
    public static DocumentNumber Unassigned => default;

    /// <summary>The text as stored and printed. Empty when unassigned.</summary>
    public string Value => _text ?? string.Empty;

    public bool IsAssigned => Value.Length > 0;

    /// <summary>True when the text has the shape <c>prefix-warehouse-yyMM-nnnn</c>; a legacy or hand-edited value loads but has no parts.</summary>
    public bool IsWellFormed => Split() is not null;

    public string? Prefix => Split()?.Prefix;

    public string? WarehouseCode => Split()?.WarehouseCode;

    public int? YearMonth => Split()?.YearMonth;

    public int? Running => Split()?.Running;

    public override string ToString() => Value;

    /// <summary>A number from its parts. Refuses a prefix or warehouse that cannot be read back, and a text longer than <see cref="Limits.DocumentNoMaxLength"/>.</summary>
    public static DocumentNumber Create(string prefix, string warehouseCode, int yearMonth, int running)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentException.ThrowIfNullOrEmpty(warehouseCode);
        if (prefix.Contains(DocumentNumberFormat.Separator, StringComparison.Ordinal))
            throw new ArgumentException("A number prefix is read up to the first separator, so it cannot contain one.", nameof(prefix));
        ArgumentOutOfRangeException.ThrowIfNegative(yearMonth);
        ArgumentOutOfRangeException.ThrowIfNegative(running);

        var text = Format(prefix, warehouseCode, yearMonth, running);
        if (text.Length > Limits.DocumentNoMaxLength)
            throw new DomainException(Messages.TextTooLong("Document number", Limits.DocumentNoMaxLength));
        return new DocumentNumber(text);
    }

    /// <summary>A stored value as it is: never throws, never changes the text. Null and the empty text are <see cref="Unassigned"/>.</summary>
    public static DocumentNumber FromStored(string? stored) => string.IsNullOrEmpty(stored) ? default : new DocumentNumber(stored);

    public static string PrefixFor(DocumentType t) => t switch
    {
        DocumentType.Receipt => DocumentNumberFormat.ReceiptPrefix,
        DocumentType.Issue => DocumentNumberFormat.IssuePrefix,
        DocumentType.Transfer => DocumentNumberFormat.TransferPrefix,
        DocumentType.Adjust => DocumentNumberFormat.AdjustPrefix,
        DocumentType.PurchaseOrder => DocumentNumberFormat.PurchaseOrderPrefix,
        DocumentType.SalesOrder => DocumentNumberFormat.SalesOrderPrefix,
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "A document type needs a number prefix of its own.")
    };

    /// <summary>BR-10: times are stored in UTC and shown in Asia/Kuala_Lumpur. Returns yyMM.</summary>
    public static int YearMonthOf(DateTime utcNow)
    {
        var local = utcNow.AddHours(Limits.MalaysiaUtcOffsetHours);
        return local.Year % 100 * 100 + local.Month;
    }

    /// <summary>The printed number, without a length check (<see cref="Create"/> checks it).</summary>
    public static string Format(string prefix, string warehouseCode, int yearMonth, int running) =>
        string.Join(DocumentNumberFormat.Separator, prefix, warehouseCode,
            yearMonth.ToString(DocumentNumberFormat.YearMonthFormat), running.ToString(DocumentNumberFormat.RunningFormat));

    private Parts? Split()
    {
        var segments = Value.Split(DocumentNumberFormat.Separator);
        // prefix, at least one warehouse segment, yyMM, running
        if (segments.Length < 4) return null;
        if (!TryDigits(segments[^2], out var yearMonth) || !TryDigits(segments[^1], out var running)) return null;

        var prefix = segments[0];
        var warehouse = string.Join(DocumentNumberFormat.Separator, segments[1..^2]);
        return prefix.Length == 0 || warehouse.Length == 0 ? null : new Parts(prefix, warehouse, yearMonth, running);
    }

    private static bool TryDigits(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private readonly record struct Parts(string Prefix, string WarehouseCode, int YearMonth, int Running);
}
