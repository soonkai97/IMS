namespace Ims.Application;

/// <summary>Reads state. Must not change anything observable.</summary>
public interface IQuery<out TResult> : IRequest<TResult>;
