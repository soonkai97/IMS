using Ims.Application;
using Ims.Application.Features.Audit;
using Ims.Application.Features.Roles;
using Ims.Application.Features.Users;
using Ims.Domain.Tests.Stock;
using Microsoft.Extensions.Logging.Abstractions;
using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Identity;

/// <summary>
/// DDD-15: <see cref="RoleAuditSubscriber"/> and <see cref="UserAuditSubscriber"/> turn each identity event into the audit row the handlers wrote before (docs/architecture/ddd-roadmap.md 2.4, 2.5). They read only the
/// facts the event carries. The rows go through the real <see cref="AuditTrail"/>, so what is compared is the exact row: action, entity type, entity id, actor, time and the raw JSON of Before and After (key order,
/// nulls and booleans included). <c>IdentityAuditRowsTests</c> (Api) proves the same rows through the whole stack.
/// </summary>
public class IdentityAuditSubscribersTests
{
    private const string Who = "caller";

    private sealed record Row(string Actor, string Action, string EntityType, string EntityId, string? Before, string? After, DateTime At);

    private readonly IdentityStore<AuditLog> _logs = new([], new Journal());

    private async Task<List<Row>> HandleAsync<TEvent>(TEvent domainEvent, object subscriber) where TEvent : IDomainEvent
    {
        await ((IDomainEventHandler<TEvent>)subscriber).HandleAsync(domainEvent, CancellationToken.None);
        return _logs.Added.Select(l => new Row(l.Actor, l.Action, l.EntityType, l.EntityId, l.Before, l.After, l.At)).ToList();
    }

    private IAuditTrail Trail() => new AuditTrail(_logs, new FullAccessCaller(1), new FakeClock(), NullLogger<AuditTrail>.Instance);

    private Task<List<Row>> HandleRoleAsync<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent => HandleAsync(domainEvent, new RoleAuditSubscriber(Trail()));

    private Task<List<Row>> HandleUserAsync<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent => HandleAsync(domainEvent, new UserAuditSubscriber(Trail()));

    private static Row Expected(string action, string type, Guid id, string? before, string? after) => new(Who, action, type, id.ToString(), before, after, Now);

    private static Role Custom(params string[] permissions) => Role.Create("Buyers", "Buyers", null, false, permissions).WithId(9);

    // ----- roles -----
    [Fact]
    public async Task RoleCreated_writes_the_code_the_name_the_permissions_in_catalog_order_and_the_source_of_a_copy_or_null()
    {
        var role = Role.Create("Buyers", "Buyers", null, false, [Permissions.StockRead, Permissions.MasterDataRead]);
        var created = Assert.IsType<RoleCreated>(Assert.Single(role.DomainEvents));
        var copied = Role.Create("Copy", "Copy", null, false, [Permissions.MasterDataRead], copiedFrom: "Buyers");

        var rows = await HandleRoleAsync(created);
        var copyRows = await HandleRoleAsync(Assert.IsType<RoleCreated>(Assert.Single(copied.DomainEvents)));

        Assert.Equal(Expected("role.created", "Role", role.PublicId, null, """{"Code":"Buyers","Name":"Buyers","Permissions":["masterdata.read","stock.read"],"CopiedFrom":null}"""), rows[0]);
        Assert.Equal(Expected("role.created", "Role", copied.PublicId, null, """{"Code":"Copy","Name":"Copy","Permissions":["masterdata.read"],"CopiedFrom":"Buyers"}"""), copyRows[1]);
    }

    [Fact]
    public async Task RoleUpdated_writes_the_text_and_state_before_and_the_code_with_the_text_and_state_after()
    {
        var role = Custom(Permissions.MasterDataRead);
        role.ClearDomainEvents();
        role.Update("Renamed", "new text", active: false);

        var rows = await HandleRoleAsync(Assert.IsType<RoleUpdated>(Assert.Single(role.DomainEvents)));

        Assert.Equal(
            [Expected("role.updated", "Role", role.PublicId, """{"Name":"Buyers","Description":"","IsActive":true}""", """{"Code":"Buyers","Name":"Renamed","Description":"new text","IsActive":false}""")],
            rows);
    }

    [Fact]
    public async Task RolePermissionsChanged_writes_what_was_removed_before_and_what_was_added_after_each_with_the_code()
    {
        var role = Custom(Permissions.MasterDataRead, Permissions.StockRead);
        role.ClearDomainEvents();
        role.SetPermissions([Permissions.MasterDataRead, Permissions.DocumentCreate, Permissions.MasterDataManage]);

        var rows = await HandleRoleAsync(Assert.IsType<RolePermissionsChanged>(Assert.Single(role.DomainEvents)));

        Assert.Equal(
            [Expected("role.permissions-changed", "Role", role.PublicId, """{"Code":"Buyers","Removed":["stock.read"]}""", """{"Code":"Buyers","Added":["masterdata.manage","document.create"]}""")],
            rows);
    }

    [Fact]
    public async Task RoleDeleted_writes_the_code_the_name_and_the_permissions_before_and_no_after()
    {
        var role = Custom(Permissions.StockRead, Permissions.MasterDataRead);
        role.ClearDomainEvents();
        role.Delete();

        var rows = await HandleRoleAsync(Assert.IsType<RoleDeleted>(Assert.Single(role.DomainEvents)));

        Assert.Equal([Expected("role.deleted", "Role", role.PublicId, """{"Code":"Buyers","Name":"Buyers","Permissions":["masterdata.read","stock.read"]}""", null)], rows);
    }

    // ----- users -----
    [Fact]
    public async Task UserCreated_writes_the_username_the_role_code_and_the_scope_after_and_no_before()
    {
        var user = User.Create("alice", "alice@example.com", "Alice", NewRole(Roles.Clerk), "secret-hash", allWarehouses: true);
        user.MarkCreated();

        var rows = await HandleUserAsync(Assert.IsType<UserCreated>(Assert.Single(user.DomainEvents)));

        Assert.Equal([Expected("user.created", "User", user.PublicId, null, """{"Username":"alice","Role":"Clerk","AllWarehouses":true}""")], rows);
        Assert.DoesNotContain("secret-hash", rows[0].After);
    }

    [Fact]
    public async Task UserUpdated_writes_the_username_role_scope_state_and_warehouses_before_and_after_and_never_the_hash()
    {
        var user = User.Create("alice", "alice@example.com", "Alice", NewRole(Roles.Clerk), "secret-hash", [1, 2]);
        user.Update("alice", "alice@example.com", "Alice", NewRole(Roles.Supervisor, id: 3), isActive: false, allWarehouses: true, warehouseIds: [4], passwordHash: "new-secret-hash");

        var rows = await HandleUserAsync(Assert.IsType<UserUpdated>(Assert.Single(user.DomainEvents)));

        Assert.Equal(
            [Expected("user.updated", "User", user.PublicId,
                """{"Username":"alice","Role":"Clerk","AllWarehouses":false,"IsActive":true,"Warehouses":[1,2]}""",
                """{"Username":"alice","Role":"Supervisor","AllWarehouses":true,"IsActive":false,"Warehouses":[4]}""")],
            rows);
        Assert.DoesNotContain("hash", $"{rows[0].Before}{rows[0].After}", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryEventOfTheIdentityRoots_HasASubscriber()
    {
        // A new event of Role or User without a handler would be a change nobody audits: the subscribers handle exactly the events the aggregates raise.
        var handled = typeof(RoleAuditSubscriber).GetInterfaces().Concat(typeof(UserAuditSubscriber).GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)).Select(i => i.GetGenericArguments()[0].Name).Order().ToList();

        Assert.Equal(["RoleCreated", "RoleDeleted", "RolePermissionsChanged", "RoleUpdated", "UserCreated", "UserUpdated"], handled);
    }
}
