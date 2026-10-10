namespace Ims.Application;

/// <summary>
/// Extension point for the API's error handling: an adapter (for example the database adapter) translates its own exception
/// types into a client-safe problem, so the presentation layer never references provider types.
/// </summary>
public interface IExceptionMapper
{
    /// <returns>A mapping, or null when this mapper does not recognise the exception.</returns>
    ProblemMapping? Map(Exception exception);
}
