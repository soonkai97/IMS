namespace Ims.Application.Features.Documents;

/// <summary>What posting a linked document carries from the check before the engine runs to the step after it. Each link kind has its own subtype.</summary>
public abstract record LinkedPostingState;
