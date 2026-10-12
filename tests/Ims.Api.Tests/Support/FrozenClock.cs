using Ims.Application;

namespace Ims.Api.Tests.Support;

/// <summary>A clock that never moves, so every row written through a host that uses it gets the same timestamp.</summary>
public sealed class FrozenClock(DateTime utcNow) : IClock
{
    public FrozenClock() : this(DateTime.UtcNow)
    {
    }

    public DateTime UtcNow { get; } = utcNow;
}
