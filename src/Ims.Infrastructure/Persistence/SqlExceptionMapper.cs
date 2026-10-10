using Ims.Application;
using Ims.Domain;
using Microsoft.Data.SqlClient;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// Translates SQL Server failures into client-safe problems. A command timeout is a 504, an unreachable or overloaded server a 503
/// (retry later), an exhausted deadlock/key-race retry a 409. Anything else is left to the generic 500 handler.
/// </summary>
public sealed class SqlExceptionMapper : IExceptionMapper
{
    public ProblemMapping? Map(Exception exception)
    {
        var sql = exception as SqlException ?? (exception as Microsoft.EntityFrameworkCore.DbUpdateException)?.InnerException as SqlException
                  ?? exception.InnerException as SqlException;
        if (sql is null) return null;

        return sql.Number switch
        {
            SqlErrors.CommandTimeout => new ProblemMapping(StatusCodes.GatewayTimeout, Messages.RequestTimedOutTitle, Messages.RequestTimedOut),
            SqlErrors.Deadlock or SqlErrors.LockTimeout or SqlErrors.UniqueIndex or SqlErrors.UniqueConstraint =>
                new ProblemMapping(StatusCodes.Conflict, Messages.ConflictTitle, Messages.ConcurrencyConflict),
            _ when SqlErrors.Unavailable.Contains(sql.Number) =>
                new ProblemMapping(StatusCodes.ServiceUnavailable, Messages.ServiceUnavailableTitle, Messages.ServiceBusy),
            _ => null
        };
    }

    /// <summary>HTTP status numbers, kept here so Infrastructure does not need an ASP.NET reference.</summary>
    private static class StatusCodes
    {
        public const int Conflict = 409;
        public const int ServiceUnavailable = 503;
        public const int GatewayTimeout = 504;
    }
}
