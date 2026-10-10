namespace Ims.Application;

/// <summary>A request that must carry a precondition (the document's version in If-Match) did not. Mapped to 428 by the API.</summary>
public class PreconditionRequiredException(string message) : Exception(message);
