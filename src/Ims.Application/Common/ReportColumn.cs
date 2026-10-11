namespace Ims.Application;

/// <summary>
/// A column of an Excel export or template. A column with a <paramref name="VisibleWith"/> permission is left out of the file for a caller who lacks it.
/// A <paramref name="IsMoney"/> column holds money: its numbers are rounded to 2 decimals and formatted <c>#,##0.00</c>.
/// </summary>
public record ReportColumn(string Header, string? VisibleWith = null, bool IsMoney = false);
