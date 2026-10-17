using System.ComponentModel.DataAnnotations;
using Ims.Domain;
namespace Ims.Infrastructure.Security;

/// <summary>Bound from the "Access" section and validated at startup.</summary>
public class AccessOptions
{
    /// <summary>How long a user's access snapshot is trusted before it is read again. A change made on this instance applies at once (the snapshot is forgotten); one made elsewhere within this time.</summary>
    [Range(1, Limits.AccessSnapshotTtlMaxSeconds)] public int SnapshotTtlSeconds { get; set; } = Limits.AccessSnapshotTtlSeconds;
}
