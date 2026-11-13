namespace Ims.Application;

/// <summary>A return line names an original line whose base quantity earlier returns have used up although entered quantity remains (409, code <c>return_line_exhausted</c>): leave the line out.</summary>
public class ReturnLineExhaustedException(string message) : ConflictException(message);
