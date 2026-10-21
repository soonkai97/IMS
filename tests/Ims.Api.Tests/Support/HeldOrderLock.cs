using Ims.Api.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Ims.Api.Tests.Support;

/// <summary>Holds a purchase order's application lock (<c>po:{id}</c>) in a transaction of its own until <see cref="CommitAsync"/>; work done through it is invisible to others until then. Disposing without committing rolls it back and lets go.</summary>
public sealed class HeldOrderLock : IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;

    private HeldOrderLock(SqlConnection connection, SqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public static async Task<HeldOrderLock> TakeAsync(ApiFixture fixture, Guid orderId)
    {
        var key = await fixture.Database.ScalarAsync<int>("SELECT Id FROM PurchaseOrder WHERE PublicId = @id", ("@id", orderId));
        var connection = new SqlConnection(fixture.Database.ConnectionString);
        await connection.OpenAsync();
        var held = new HeldOrderLock(connection, (SqlTransaction)await connection.BeginTransactionAsync());
        var granted = await held.ScalarAsync<int>($"DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'po:{key}', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0; SELECT @r");
        Assert.True(granted >= 0, "the test could not take the order's lock");
        return held;
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var command = new SqlCommand(sql, _connection, _transaction);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new SqlCommand(sql, _connection, _transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    public Task CommitAsync() => _transaction.CommitAsync();

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
