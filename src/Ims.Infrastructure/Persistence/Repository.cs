using Ardalis.Specification.EntityFrameworkCore;
using Ims.Application;
namespace Ims.Infrastructure.Persistence;

/// <summary>
/// One generic EF Core repository for every aggregate. Reads go through Ardalis specifications; writes are only staged
/// on the context, and <see cref="UnitOfWork"/> commits them.
/// </summary>
public class Repository<T>(AppDbContext db) : RepositoryBase<T>(db), IRepository<T> where T : class
{
    public void Add(T entity) => db.Set<T>().Add(entity);
    public void AddRange(IEnumerable<T> entities) => db.Set<T>().AddRange(entities);
    public void Remove(T entity) => db.Set<T>().Remove(entity);
    public void RemoveRange(IEnumerable<T> entities) => db.Set<T>().RemoveRange(entities);
}
