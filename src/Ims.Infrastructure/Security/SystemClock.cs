using Ims.Application;
namespace Ims.Infrastructure.Security;

public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
