namespace Ims.Application;

/// <summary>A request with a result. Use <see cref="ICommand{TResult}"/> to change state and <see cref="IQuery{TResult}"/> to read it.</summary>
public interface IRequest<out TResult>;
