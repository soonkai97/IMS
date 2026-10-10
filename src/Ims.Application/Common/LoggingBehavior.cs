using System.Diagnostics;
using Ims.Domain;
using Microsoft.Extensions.Logging;

namespace Ims.Application;

/// <summary>
/// Logs every request with its kind, caller, duration and outcome. Business rejections are expected and logged at Debug;
/// only unexpected failures are warnings. Exceptions are rethrown untouched: the API logs the stack once.
/// </summary>
public sealed class LoggingBehavior(ILogger<LoggingBehavior> log, ICurrentUser current) : IPipelineBehavior
{
    public async Task<TResult> HandleAsync<TResult>(IRequest<TResult> request, Func<Task<TResult>> next, CancellationToken ct)
    {
        var name = request.GetType().Name;
        var kind = request is IQuery<TResult> ? RequestKinds.Query : RequestKinds.Command;
        var level = kind == RequestKinds.Command ? LogLevel.Information : LogLevel.Debug;
        var user = current.IsAuthenticated ? current.Username : AuditEntities.Anonymous;
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await next();
            log.Log(level, "{Kind} {Request} by {User} succeeded in {Elapsed} ms", kind, name, user, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            var expected = ex is DomainException or ValidationException or NotFoundException or ForbiddenException
                or ConflictException or UnauthorizedException or ConcurrencyException or OperationCanceledException;
            log.Log(expected ? LogLevel.Debug : LogLevel.Warning, "{Kind} {Request} by {User} failed after {Elapsed} ms: {ExceptionType}",
                kind, name, user, sw.ElapsedMilliseconds, ex.GetType().Name);
            throw;
        }
    }
}
