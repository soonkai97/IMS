namespace Ims.Domain;

/// <summary>
/// A named business rule. Rules are small, pure and testable on their own; entities and domain services
/// check them with <see cref="BusinessRules.Ensure"/> instead of scattering <c>if (...) throw</c> blocks.
/// </summary>
public interface IBusinessRule
{
    string Message { get; }
    bool IsBroken();
}
