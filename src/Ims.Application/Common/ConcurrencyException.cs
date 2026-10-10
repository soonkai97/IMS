namespace Ims.Application;

/// <summary>Another request changed the same data first. Retriable; surfaced as 409 if retries run out.</summary>
public class ConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
