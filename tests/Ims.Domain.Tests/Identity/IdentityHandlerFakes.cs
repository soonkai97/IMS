using System.Reflection;
using Ardalis.Specification;
using Ims.Application;
using Ims.Application.Features.Roles;
using Ims.Application.Features.Users;
using Ims.Domain.Tests.Stock;

namespace Ims.Domain.Tests.Identity;

/// <summary>
/// Hand-written stand-ins so a user or role handler runs without a database and writes down, in one journal, what it asked for and in what order (the roles lock, the count of the other
/// administrators, the save). A member a test does not need throws, so a handler that starts asking for more fails loudly. The audit trail is a no-op: where the audit row is written is not what these
/// tests look at (<c>IdentityAuditRowsTests</c> in Ims.Api.Tests pins the rows), and a handler is built by the types of its constructor, so taking the audit trail out of it later changes nothing here.
/// </summary>
internal static class IdentityHandlerFakes
{
    public const string LockRoles = "lock roles";
    public const string CountAdministrators = "count administrators";
    public const string ListTokens = "list tokens";
    public const string Save = "save";

    /// <summary>Builds a handler from whichever of <paramref name="services"/> its constructor asks for, by type.</summary>
    public static THandler Build<THandler>(params object[] services)
    {
        var constructor = typeof(THandler).GetConstructors().Single();
        var arguments = constructor.GetParameters().Select(p => services.FirstOrDefault(s => p.ParameterType.IsInstanceOfType(s)) ?? throw new InvalidOperationException($"No fake for {p.ParameterType.Name}")).ToArray();
        return (THandler)constructor.Invoke(arguments);
    }
}

/// <summary>Holds the rows and answers reads by running the handler's own specification over them, so the filter under test is the real one. The count of the other administrators is written in the journal.</summary>
internal sealed class IdentityStore<T>(IEnumerable<T> rows, Journal journal) : IRepository<T> where T : class
{
    private readonly List<T> _rows = rows.ToList();

    /// <summary>What was staged through <c>Add</c>: the audit rows a subscriber wrote.</summary>
    public List<T> Added { get; } = [];

    public Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) => Task.FromResult(specification.Evaluate(_rows).FirstOrDefault());

    public Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default) => Task.FromResult(specification.Evaluate(_rows).FirstOrDefault());

    public Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        if (specification is ActiveAdministratorsSpec) journal.Add(IdentityHandlerFakes.CountAdministrators);
        return Task.FromResult(specification.Evaluate(_rows).Any());
    }

    public Task<List<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        if (typeof(T) == typeof(RefreshToken)) journal.Add(IdentityHandlerFakes.ListTokens);
        return Task.FromResult(specification.Evaluate(_rows).ToList());
    }

    public void Add(T entity) => Added.Add(entity);
    public void AddRange(IEnumerable<T> entities) => Added.AddRange(entities);
    public void Remove(T entity) => throw new NotSupportedException();
    public void RemoveRange(IEnumerable<T> entities) => throw new NotSupportedException();
    public Task<List<T>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<List<TResult>> ListAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<T?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull => throw new NotSupportedException();
    public Task<T?> SingleOrDefaultAsync(ISingleResultSpecification<T> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<T, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<T> AsAsyncEnumerable(ISpecification<T> specification) => throw new NotSupportedException();
}

internal sealed class IdentityLocker(Journal journal) : ITransactionLocker
{
    public Task LockRolesAsync(CancellationToken ct)
    {
        journal.Add(IdentityHandlerFakes.LockRoles);
        return Task.CompletedTask;
    }

    public Task LockStockAsync(int warehouseId, IReadOnlyCollection<int> productIds, CancellationToken ct) => throw new NotSupportedException();
    public Task LockDocumentSequenceAsync(string prefix, int yearMonth, int warehouseId, CancellationToken ct) => throw new NotSupportedException();
    public Task LockModulesAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task LockModulesSharedAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task LockPurchaseOrderAsync(int orderId, CancellationToken ct) => throw new NotSupportedException();
    public Task LockSalesOrderAsync(int orderId, CancellationToken ct) => throw new NotSupportedException();
    public Task LockReturnOriginAsync(int documentId, CancellationToken ct) => throw new NotSupportedException();
}

internal sealed class IdentityWork(Journal journal) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        journal.Add(IdentityHandlerFakes.Save);
        return Task.FromResult(1);
    }

    public Task<T> ExecuteAsync<T>(Func<Task<T>> work, CancellationToken ct = default) => work();
}

internal sealed class NoAudit : IAuditTrail
{
    public void Record(string action, string entityType, string entityId, object? before = null, object? after = null, string? actor = null) { }
}

/// <summary>A caller who may do everything and sees every warehouse, so the containment checks pass and the continuity rule is what is left to decide.</summary>
internal sealed class FullAccessCaller(int userId) : ICurrentUser
{
    public bool IsAuthenticated => true;
    public int UserId => userId;
    public string Username => "caller";
    public string Role => "Operator";
    public IReadOnlyCollection<int> WarehouseIds => [];
    public bool CanAccessWarehouse(int warehouseId) => true;
    public bool AllWarehouses => true;
    public ActorRights Rights() => SystemRoleDefaults.RightsFor(Roles.Admin, userId);
}

internal sealed class FakeHasher : IPasswordHasher
{
    public string Hash(string password) => "hash:" + password;
    public bool Verify(string hash, string password) => hash == "hash:" + password;
}

internal sealed class NoWarehouses : IIdMap<Warehouse>
{
    public Task<IReadOnlyDictionary<Guid, int>> ToInternalAsync(IEnumerable<Guid> publicIds, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());
    public Task<IReadOnlyDictionary<int, Guid>> ToPublicAsync(IEnumerable<int> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<int, Guid>>(new Dictionary<int, Guid>());
}

internal sealed class EverythingOffered : Ims.Application.Features.Modules.IModuleSwitches
{
    public Task<bool> IsEnabledAsync(string code, CancellationToken ct) => Task.FromResult(true);
    public Task<IReadOnlyList<string>> EnabledAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(ModuleCatalog.Codes.ToList());
    public void Invalidate() { }
}

internal sealed class StubRoleReader : IRoleReader
{
    public Task<IReadOnlyList<RoleDto>> ListAsync(RoleFilter filter, CancellationToken ct) => throw new NotSupportedException();
    public Task<RoleDto?> FindAsync(Guid publicId, CancellationToken ct) => Task.FromResult<RoleDto?>(new RoleDto(publicId, "", "", "", false, true, [], 0, ""));
}

internal sealed class AnyVersion : IRoleVersion
{
    public string Token(Guid publicId, byte[] rowVersion) => "";
    public void EnsureCurrent(Role role, string? expectedToken) { }
}
