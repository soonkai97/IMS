using System.Data.Common;
using Ims.Application;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// Adds a SQL Server query hint to a query tagged with one of the <see cref="QueryHint"/> (<c>query.TagWith(QueryHint.Recompile)</c>). SQL Server compiles a
/// parameterised statement once, for the first value that arrives, and reuses that plan for every other value. Where the best plan depends on the value, that is
/// wrong for most values, and EF Core has no query hints of its own:
/// <list type="bullet">
/// <item><see cref="QueryHint.Recompile"/> adds <c>OPTION (RECOMPILE)</c>: a plan of its own for every call, for a filter whose best plan depends on how many rows
/// the value matches (the movements report for one product: the plan for a product with thousands of lines walks every document when the product has none).
/// It pays a compile for each call (roughly 0.1 to 0.8 s of CPU for that report, docs/dba/README.md 4b).</item>
/// <item><see cref="QueryHint.NoSniffing"/> adds <c>OPTION (USE HINT('DISABLE_PARAMETER_SNIFFING'))</c>: one plan, compiled once from the average statistics and
/// reused for every value, for a statement whose plan flips with the size of the page (the documents list, docs/dba/README.md 4d). No compile per call.</item>
/// </list>
/// Untagged queries are not touched. A command that already ends with the hint is left alone.
/// </summary>
public sealed class QueryHintInterceptor : DbCommandInterceptor
{
    private static readonly (string Comment, string Hint)[] Hints =
    [
        ("-- " + QueryHint.Recompile, "\nOPTION (RECOMPILE)"),
        ("-- " + QueryHint.NoSniffing, "\nOPTION (USE HINT('DISABLE_PARAMETER_SNIFFING'))"),
    ];

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
        var text = command.CommandText;
        foreach (var (comment, hint) in Hints)
        {
            if (!text.StartsWith(comment, StringComparison.Ordinal)) continue;
            if (!text.EndsWith(hint, StringComparison.Ordinal)) command.CommandText = text + hint;
            return;
        }
    }
}
