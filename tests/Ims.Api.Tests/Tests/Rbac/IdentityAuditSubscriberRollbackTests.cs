using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>
/// DDD-15 against real SQL Server: the audit row that <c>RoleAuditSubscriber</c> stages for an event commits or rolls back WITH the business change (AUTH-03: an audit row cannot be lost, and no row describes a
/// change that did not happen), and a retried attempt leaves one committed row. The host is the real one, so the real subscriber runs; the unit of work is driven directly with real roles. The rows of every verb
/// through the HTTP pipeline are pinned by <c>IdentityAuditRowsTests</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class IdentityAuditSubscriberRollbackTests(ApiFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _host = null!;

    public Task InitializeAsync()
    {
        _host = fixture.CreateHost(_ => { });
        _ = _host.Services;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task RunAsync(Func<IServiceProvider, Task> body)
    {
        using var scope = _host.Services.CreateScope();
        await body(scope.ServiceProvider);
    }

    /// <summary>A new custom role, tracked as Added: raises <see cref="RoleCreated"/>, which the subscriber turns into a row at the next save.</summary>
    private static Role StageRole(IServiceProvider sp)
    {
        var role = Role.Create(Unique.Code("R"), Unique.Name("Role"), null, false, [Permissions.MasterDataRead]);
        sp.GetRequiredService<IRepository<Role>>().Add(role);
        return role;
    }

    private Task<int> RolesAsync(Guid publicId) => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM [Role] WHERE PublicId = @p", ("@p", publicId));

    private Task<int> CreatedRowsAsync(Guid publicId) =>
        fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityType = @t AND EntityId = @id", ("@a", AuditActions.RoleCreated), ("@t", AuditEntities.Role), ("@id", publicId.ToString()));

    [Fact]
    public async Task A_saved_role_has_exactly_one_Created_row_that_the_subscriber_staged_for_the_event()
    {
        Role role = null!;

        await RunAsync(async sp =>
        {
            role = StageRole(sp);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        });

        Assert.Equal(1, await RolesAsync(role.PublicId));
        Assert.Equal(1, await CreatedRowsAsync(role.PublicId));
    }

    [Fact]
    public async Task When_a_later_save_of_the_transaction_fails_the_role_and_the_row_the_subscriber_staged_are_rolled_back_with_it()
    {
        Role first = null!;

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                first = StageRole(sp);
                await uow.SaveChangesAsync();                                                   // the role and its Created row are written inside the transaction
                Assert.Equal(1, await RowsInThisTransactionAsync(sp, first.PublicId));          // the subscriber's row is part of the same transaction

                var db = sp.GetRequiredService<AppDbContext>();
                var clerk = await db.Set<Role>().SingleAsync(r => r.Code == Roles.Clerk);
                var user = User.Create(new string('x', 400), "x@example.test", "X", clerk, "hash");   // too long for the column: the database refuses the second save
                sp.GetRequiredService<IRepository<User>>().Add(user);
                await uow.SaveChangesAsync();
                return 0;
            });
        }));

        Assert.Equal((0, 0), (await RolesAsync(first.PublicId), await CreatedRowsAsync(first.PublicId)));
    }

    [Fact]
    public async Task A_retried_attempt_leaves_one_role_and_one_Created_row_and_none_for_the_attempt_that_was_discarded()
    {
        var attempts = new List<Role>();

        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                attempts.Add(StageRole(sp));                                                    // built inside the body: a retry builds a new one
                await uow.SaveChangesAsync();
                if (attempts.Count == 1) throw new ConcurrencyException(Messages.ConcurrencyConflict);   // the first attempt is rolled back whole, row included
                return 0;
            });
        });

        Assert.Equal(2, attempts.Count);
        Assert.Equal((0, 0), (await RolesAsync(attempts[0].PublicId), await CreatedRowsAsync(attempts[0].PublicId)));
        Assert.Equal((1, 1), (await RolesAsync(attempts[1].PublicId), await CreatedRowsAsync(attempts[1].PublicId)));
    }

    /// <summary>Counts the Created rows of the role on the connection of the open transaction: not visible to another connection until it commits.</summary>
    private static async Task<int> RowsInThisTransactionAsync(IServiceProvider sp, Guid publicId) =>
        await sp.GetRequiredService<AppDbContext>().Set<AuditLog>().CountAsync(l => l.Action == AuditActions.RoleCreated && l.EntityId == publicId.ToString());
}
