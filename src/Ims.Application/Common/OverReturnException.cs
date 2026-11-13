namespace Ims.Application;

/// <summary>A return asks, at posting, for more of an original line than can still be returned (409, code <c>over_return</c>). The locked check: the one at save and submit is advisory.</summary>
public class OverReturnException(string message, IReadOnlyList<OverReturnLine> lines) : ConflictException(message)
{
    public IReadOnlyList<OverReturnLine> Lines { get; } = lines;
}
