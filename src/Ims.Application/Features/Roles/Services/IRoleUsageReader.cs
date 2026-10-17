namespace Ims.Application.Features.Roles;

/// <summary>How many live users hold each role. Implemented in Infrastructure because a grouped count is not expressible through specifications.</summary>
public interface IRoleUsageReader
{
    /// <summary>The number of live users (active or not) per role id; a role nobody holds is absent from the result.</summary>
    Task<IReadOnlyDictionary<int, int>> CountUsersAsync(IReadOnlyCollection<int> roleIds, CancellationToken ct);
}
