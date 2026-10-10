namespace Ims.Application;

/// <summary>A column of an Excel export or template. A column with a <paramref name="VisibleWith"/> permission is left out of the file for a caller who lacks it.</summary>
public record ReportColumn(string Header, string? VisibleWith = null);
