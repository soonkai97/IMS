using Ims.Domain;
namespace Ims.Application;

/// <summary>Reads a user's access snapshot from the database, never from a cache: sign-in and refresh call it, because the token they write is what carries the rights.</summary>
public interface IAccessSnapshotLoader
{
    /// <summary>Null when the user does not exist, is inactive or has no live role.</summary>
    Task<AccessSnapshot?> LoadAsync(int userId, CancellationToken ct);
}
