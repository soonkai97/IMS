namespace Ims.Application;

/// <summary>
/// A lock that serialises writers (the roles lock) stayed busy for the whole wait. Answered at once as 409, not retried: the holder is slow, and five more waits would
/// only keep the caller hanging until the request times out. A <see cref="ConcurrencyException"/> to everything that handles one, except the retry.
/// </summary>
public class LockTimeoutException(string message) : ConcurrencyException(message);
