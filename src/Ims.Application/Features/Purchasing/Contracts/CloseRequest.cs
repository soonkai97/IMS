namespace Ims.Application.Features.Purchasing;

/// <param name="Reason">Why what is still to come will not come: required, at least 5 characters once trimmed.</param>
public sealed record CloseRequest(string? Reason);
