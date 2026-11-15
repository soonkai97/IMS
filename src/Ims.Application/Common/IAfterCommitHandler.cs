using Ims.Domain;

namespace Ims.Application;

/// <summary>
/// Reacts to a domain event once, AFTER the outermost transaction has committed, for effects that must never roll a business change back (cache invalidation, a future notification).
/// A failure is logged and swallowed: the request has already succeeded. It runs only for the attempt that committed (nothing is delivered for an attempt that was retried), and like an
/// in-transaction handler it must not call <c>IUnitOfWork.SaveChangesAsync</c>.
/// </summary>
public interface IAfterCommitHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}
