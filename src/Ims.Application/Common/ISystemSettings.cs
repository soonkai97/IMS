using Ims.Domain;
namespace Ims.Application;

public interface ISystemSettings
{
    Task<SystemSettings> GetAsync(CancellationToken ct);

    /// <summary>Settings are read once per request and remembered; call this after changing them.</summary>
    void Invalidate();
}
