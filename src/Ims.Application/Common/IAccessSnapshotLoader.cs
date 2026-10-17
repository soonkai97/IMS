using Ims.Domain;
namespace Ims.Application;

/// <summary>Reads a user's access snapshot from the database. Uncached: the cache sits in front of it, sign-in and the user's own profile read it directly.</summary>
public interface IAccessSnapshotLoader
{
    /// <summary>Null when the user does not exist, is inactive or has no live role.</summary>
    Task<AccessSnapshot?> LoadAsync(int userId, CancellationToken ct);
}
