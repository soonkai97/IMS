namespace Ims.Domain;

/// <summary>A business rule was violated. Mapped to HTTP 400 by the API.</summary>
public class DomainException(string message) : Exception(message);
