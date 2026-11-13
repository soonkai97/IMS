using Ims.Application;
namespace Ims.Infrastructure.Security;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
