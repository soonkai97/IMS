namespace Ims.Application;

public sealed class ForbiddenException(string message) : Exception(message);
