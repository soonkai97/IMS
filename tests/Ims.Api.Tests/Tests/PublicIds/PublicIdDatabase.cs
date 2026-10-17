using System.Data.Common;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ims.Api.Tests.Tests.PublicIds;

/// <summary>A migrated, empty throw-away database for the tests of one class, so they may add and delete rows without disturbing the shared API database.</summary>
public sealed class PublicIdDatabase : IAsyncLifetime
{
    public TestDatabase Database { get; } = TestDatabase.CreateNew();

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await Database.DropAsync();

    public AppDbContext NewContext(CommandCounter? counter = null, Ims.Application.ICurrentUser? user = null, Ims.Application.IClock? clock = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Database.ConnectionString);
        if (counter is not null) options.AddInterceptors(counter);
        return new AppDbContext(options.Options, clock ?? new FrozenClock(), user);
    }

    /// <summary>Counts the queries a context sends, to show that a list of ids costs one.</summary>
    public sealed class CommandCounter : DbCommandInterceptor
    {
        public int Queries { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Queries++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
