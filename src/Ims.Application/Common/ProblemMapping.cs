namespace Ims.Application;

/// <summary>How an exception the Application layer does not know should be shown to the client.</summary>
public record ProblemMapping(int Status, string Title, string Detail);
