namespace Ims.Infrastructure;

public static class SqlErrors
{
    /// <summary>Unique index violation.</summary>
    public const int UniqueIndex = 2601;
    /// <summary>Primary key / unique constraint violation.</summary>
    public const int UniqueConstraint = 2627;
    /// <summary>This session was chosen as the deadlock victim. Safe to retry the whole transaction.</summary>
    public const int Deadlock = 1205;
    /// <summary>Client-side command timeout (the command ran longer than CommandTimeout).</summary>
    public const int CommandTimeout = -2;
    /// <summary>Connection-level failures where retrying later is the right answer (network, login, resource limits, failover).</summary>
    public static readonly HashSet<int> Unavailable = [-1, 53, 233, 4060, 10053, 10054, 10060, 10928, 10929, 40197, 40501, 40613, 49918, 49919, 49920];
    public const int LockTimeout = 1222;
}
