namespace Ims.Application;

public sealed class NotFoundException(string message) : Exception(message);
