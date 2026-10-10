namespace Ims.Application;

/// <summary>
/// Read access plus staging of changes. Nothing is written until <see cref="IUnitOfWork.SaveChangesAsync"/>,
/// so a use case commits all of its changes (including audit rows) together.
/// </summary>
public interface IRepository<T> : IReadRepository<T> where T : class
{
    void Add(T entity);
    void AddRange(IEnumerable<T> entities);
    void Remove(T entity);
    void RemoveRange(IEnumerable<T> entities);
}
