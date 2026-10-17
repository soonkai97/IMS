using Ims.Application;

namespace Ims.Api.Tests.Support;

/// <summary>A clock a test moves by hand, so a test can let an access token run out without waiting for it.</summary>
public sealed class MovableClock : IClock
{
    public DateTime UtcNow { get; private set; } = DateTime.UtcNow;

    public void Advance(TimeSpan by) => UtcNow += by;
}
