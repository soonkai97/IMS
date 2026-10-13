using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ims.Api.Tests.Support;

/// <summary>
/// Keeps the text of every command the context ran, as the database received it. Register it on a test host with
/// <c>services.ConfigureDbContext&lt;AppDbContext&gt;(o =&gt; o.AddInterceptors(log))</c>: it is added AFTER the interceptors of <c>AddInfrastructure</c>, so the text it sees
/// already carries what they appended (a query hint).
/// </summary>
public sealed class CommandTextLog : DbCommandInterceptor
{
    private readonly List<string> _texts = [];

    /// <summary>The commands run so far that contain <paramref name="fragment"/>.</summary>
    public IReadOnlyList<string> Containing(string fragment)
    {
        lock (_texts) return _texts.Where(t => t.Contains(fragment, StringComparison.Ordinal)).ToList();
    }

    public void Clear()
    {
        lock (_texts) _texts.Clear();
    }

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

    private void Add(DbCommand command)
    {
        lock (_texts) _texts.Add(command.CommandText);
    }
}
