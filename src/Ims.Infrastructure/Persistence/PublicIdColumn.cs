using System.Text.RegularExpressions;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// Everything the database says about <c>PublicEntity.PublicId</c>, in one place: the unique index of each table (<c>UX_&lt;Table&gt;_PublicId</c>),
/// its fill factor, and the column default that keeps a raw INSERT (seed scripts, tests) valid.
/// </summary>
public static partial class PublicIdColumn
{
    private const string IndexPrefix = "UX_";
    private const string IndexSuffix = "_PublicId";

    /// <summary>Random, like the id the application makes (<c>Guid.NewGuid()</c>): NEWSEQUENTIALID() would reveal how many rows were made between two ids.</summary>
    public const string DefaultSql = "NEWID()";

    /// <summary>
    /// Leaf pages of the index are filled to 90 % when it is built, which leaves room for the random inserts that follow. The index is narrow (16 bytes a row) and the
    /// tables are small to medium, so a periodic reorganise is the cheapest maintenance (<c>docs/dba/07-maintenance.sql</c>).
    /// </summary>
    public const int FillFactor = 90;

    public static string IndexNameOf(string table) => $"{IndexPrefix}{table}{IndexSuffix}";

    /// <summary>True when the text of a SQL Server unique-index error (2601) names one of the PublicId indexes, for example <c>unique index 'UX_Product_PublicId'</c>.</summary>
    public static bool IsViolation(string errorMessage) => IndexInMessage().IsMatch(errorMessage);

    [GeneratedRegex($"'{IndexPrefix}[A-Za-z0-9]+{IndexSuffix}'", RegexOptions.CultureInvariant)]
    private static partial Regex IndexInMessage();
}
