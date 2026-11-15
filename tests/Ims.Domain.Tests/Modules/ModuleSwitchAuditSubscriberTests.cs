using Ardalis.Specification;
using Ims.Application;
using Ims.Application.Features.Audit;
using Ims.Application.Features.Modules;
using Ims.Domain.Tests.Stock;
using Microsoft.Extensions.Logging.Abstractions;
using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Modules;

/// <summary>
/// DDD-14: <see cref="ModuleSwitchAuditSubscriber"/> turns <see cref="ModuleSwitched"/> into the audit row <c>SwitchModuleHandler</c> wrote before (docs/architecture/ddd-roadmap.md 2.4, 2.5). It reads only the facts the
/// event carries. The row goes through the real <see cref="AuditTrail"/>, so what is compared is the exact row: action, entity type, entity id (the settings key), actor, time and the raw JSON of Before and After (key order,
/// the encoding of the reason, the booleans). <c>SwitchModuleHandlerTests</c> pins the same rows through the handler, and <c>ModulesApiTests</c> through the whole stack.
/// </summary>
public class ModuleSwitchAuditSubscriberTests
{
    private const string Who = "admin";

    private sealed class Logs : IRepository<AuditLog>
    {
        public List<AuditLog> Added { get; } = [];

        public void Add(AuditLog entity) => Added.Add(entity);
        public void AddRange(IEnumerable<AuditLog> entities) => Added.AddRange(entities);
        public void Remove(AuditLog entity) => throw new NotSupportedException();
        public void RemoveRange(IEnumerable<AuditLog> entities) => throw new NotSupportedException();
        public Task<AuditLog?> FirstOrDefaultAsync(ISpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(ISpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<AuditLog>> ListAsync(ISpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<AuditLog>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<TResult>> ListAsync<TResult>(ISpecification<AuditLog, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuditLog?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull => throw new NotSupportedException();
        public Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<AuditLog, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuditLog?> SingleOrDefaultAsync(ISingleResultSpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<AuditLog, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountAsync(ISpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<AuditLog> AsAsyncEnumerable(ISpecification<AuditLog> specification) => throw new NotSupportedException();
    }

    private sealed class Caller : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public int UserId => 1;
        public string Username => Who;
        public string Role => throw new NotSupportedException();
        public IReadOnlyCollection<int> WarehouseIds => throw new NotSupportedException();
        public bool CanAccessWarehouse(int warehouseId) => throw new NotSupportedException();
        public bool AllWarehouses => true;
        public ActorRights Rights() => throw new NotSupportedException();
    }

    private readonly Logs _logs = new();

    private async Task<AuditLog> HandleAsync(ModuleSwitched domainEvent)
    {
        IDomainEventHandler<ModuleSwitched> subscriber = new ModuleSwitchAuditSubscriber(new AuditTrail(_logs, new Caller(), new FakeClock(), NullLogger<AuditTrail>.Instance));
        await subscriber.HandleAsync(domainEvent, CancellationToken.None);
        return Assert.Single(_logs.Added);
    }

    [Fact]
    public async Task TurningOn_IsTheEnabledRow_WithTheStateBeforeTheReasonAndTheRolesRegaining()
    {
        var row = await HandleAsync(new ModuleSwitched(ModuleCodes.Purchasing, EnabledBefore: false, EnabledAfter: true, "Go live on Monday", RolesRegainingGrants: 2));

        Assert.Equal((Who, AuditActions.ModuleEnabled, AuditEntities.SystemSetting, "Module.purchasing", Now), (row.Actor, row.Action, row.EntityType, row.EntityId, row.At));
        Assert.Equal("""{"Module":"purchasing","Enabled":false}""", row.Before);
        Assert.Equal("""{"Module":"purchasing","Enabled":true,"Reason":"Go live on Monday","RolesRegainingGrants":2}""", row.After);
    }

    [Fact]
    public async Task TurningOff_IsTheDisabledRow()
    {
        var row = await HandleAsync(new ModuleSwitched(ModuleCodes.Sales, EnabledBefore: true, EnabledAfter: false, "Not selling this quarter", RolesRegainingGrants: 0));

        Assert.Equal((AuditActions.ModuleDisabled, "Module.sales"), (row.Action, row.EntityId));
        Assert.Equal("""{"Module":"sales","Enabled":true}""", row.Before);
        Assert.Equal("""{"Module":"sales","Enabled":false,"Reason":"Not selling this quarter","RolesRegainingGrants":0}""", row.After);
    }

    [Fact]
    public async Task TheReasonIsEncodedByTheAuditTrail_NotByTheSubscriber()
    {
        var row = await HandleAsync(new ModuleSwitched(ModuleCodes.Returns, EnabledBefore: false, EnabledAfter: true, "Owner's call <urgent> \"now\" é", RolesRegainingGrants: 1));

        Assert.Equal("""{"Module":"returns","Enabled":true,"Reason":"Owner@u0027s call @u003Curgent@u003E @u0022now@u0022 @u00E9","RolesRegainingGrants":1}""".Replace('@', (char)92), row.After);
    }
}
