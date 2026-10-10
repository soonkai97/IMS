namespace Ims.Application;

/// <summary>How many requests are in flight in this scope (one HTTP request), so only the outermost response is shaped.</summary>
public sealed class ShapingState
{
    public int Depth { get; set; }
}
