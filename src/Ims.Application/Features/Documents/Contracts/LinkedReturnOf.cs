namespace Ims.Application.Features.Documents;

/// <summary>The document a return was created from: its id and number. Omitted while the Returns module is off, and for a caller who may not create returns.</summary>
public record LinkedReturnOf(Guid Id, string No);
