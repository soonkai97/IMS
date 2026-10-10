namespace Ims.Application;

public interface IClock
{
    DateTime UtcNow { get; }
}
