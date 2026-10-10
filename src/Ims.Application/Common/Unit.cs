namespace Ims.Application;

/// <summary>Result of a command that returns nothing.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
