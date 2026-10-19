using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ims.Api.Tests.Support;

/// <summary>
/// Counts every statement the context sends to the database, whichever way it is run (reader, non-query or scalar), and keeps its text so a failed comparison can show what was sent.
/// Register it on a test host with <c>services.ConfigureDbContext&lt;AppDbContext&gt;(o =&gt; o.AddInterceptors(log))</c>. Unlike <see cref="CommandTextLog"/> (which keeps readers
/// only, for text searches) this is a counter: a statement that slips in through another path would otherwise go unnoticed.
/// </summary>
public sealed partial class SqlStatementLog : DbCommandInterceptor
{
    /// <summary>A write inside a command: EF sends the inserts and updates of one save as a single command, so counting commands alone would not see a row more.</summary>
    [GeneratedRegex(@"\b(INSERT INTO|UPDATE|DELETE FROM|MERGE)\b")]
    private static partial Regex WriteStatement();

    private readonly ConcurrentQueue<string> _texts = new();

    /// <summary>Commands sent (database round trips).</summary>
    public int Count => _texts.Count;

    /// <summary>Insert, update and delete statements inside those commands.</summary>
    public int WriteCount => _texts.Sum(t => WriteStatement().Count(t));

    public IReadOnlyList<string> Texts => [.. _texts];

    public void Clear() => _texts.Clear();

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Add(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Add(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Add(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Add(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Add(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Add(command);
        return ValueTask.FromResult(result);
    }

    private void Add(DbCommand command) => _texts.Enqueue(command.CommandText);
}
