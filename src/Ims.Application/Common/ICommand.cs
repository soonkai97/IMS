namespace Ims.Application;

/// <summary>Changes state. May have side effects; one handler, one transaction boundary.</summary>
public interface ICommand<out TResult> : IRequest<TResult>;
