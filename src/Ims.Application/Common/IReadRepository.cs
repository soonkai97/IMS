using Ardalis.Specification;
namespace Ims.Application;

/// <summary>Read-only access to an aggregate through specifications. Use for queries.</summary>
public interface IReadRepository<T> : IReadRepositoryBase<T> where T : class;
