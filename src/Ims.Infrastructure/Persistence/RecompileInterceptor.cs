using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// Adds <c>OPTION (RECOMPILE)</c> to queries tagged with <see cref="Tag"/> (<c>query.TagWith(RecompileInterceptor.Tag)</c>). SQL Server compiles a
/// parameterised statement once, for the first value that arrives, and reuses that plan for every other value. For a filter whose best plan depends
/// on how many rows the value matches (the movements report for one product) that is wrong for most values: the plan that is right for a product with
/// thousands of lines walks every document when the product has none (millions of reads). A tagged query gets a plan of its own, for its own value, and
/// pays a compile (about 50 ms) for it. Untagged queries are not touched.
/// </summary>
public sealed class RecompileInterceptor : DbCommandInterceptor
{
    public const string Tag = "ims:recompile";
    private const string TagComment = "-- " + Tag;
    private const string Hint = "\nOPTION (RECOMPILE)";

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Mark(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Mark(command);
        return ValueTask.FromResult(result);
    }

    private static void Mark(DbCommand command)
    {
        if (command.CommandText.StartsWith(TagComment, StringComparison.Ordinal)) command.CommandText += Hint;
    }
}
