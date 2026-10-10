using Microsoft.Data.SqlClient;

namespace Ims.Api.Tests.Harness;

/// <summary>
/// A uniquely named SQL Server database that exists only for the test run. The server comes from the environment variable
/// <see cref="EnvironmentVariable"/> (CI: a SQL Server container) and defaults to the local LocalDB instance.
/// Whatever database the connection string names is ignored: this class always uses its own.
/// </summary>
public sealed class TestDatabase
{
    public const string EnvironmentVariable = "IMS_TEST_DB";
    public const string LocalServerConnection = @"Server=(localdb)\ImsDev;Trusted_Connection=True;TrustServerCertificate=True";
    private const string NamePrefix = "ImsApiTests_";
    private const string MasterDatabase = "master";

    private readonly string _masterConnection;

    private TestDatabase(string name, string connectionString, string masterConnection)
    {
        Name = name;
        ConnectionString = connectionString;
        _masterConnection = masterConnection;
    }

    public string Name { get; }

    /// <summary>Connection string pointing at the test database.</summary>
    public string ConnectionString { get; }

    public static TestDatabase CreateNew()
    {
        var server = Environment.GetEnvironmentVariable(EnvironmentVariable);
        var builder = new SqlConnectionStringBuilder(string.IsNullOrWhiteSpace(server) ? LocalServerConnection : server);

        var name = NamePrefix + Guid.NewGuid().ToString("N");
        builder.InitialCatalog = MasterDatabase;
        var master = builder.ConnectionString;
        builder.InitialCatalog = name;
        return new TestDatabase(name, builder.ConnectionString, master);
    }

    /// <summary>Drops the database, disconnecting anyone still attached. Never throws: cleanup must not hide test results.</summary>
    public async Task DropAsync()
    {
        SqlConnection.ClearAllPools();
        try
        {
            await using var connection = new SqlConnection(_masterConnection);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Name}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Name}];
                END
                """;
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Could not drop test database {Name}: {e.Message}");
        }
    }

    public async Task<int> ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = Build(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = Build(connection, sql, parameters);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default! : (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    private static SqlCommand Build(SqlConnection connection, string sql, (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
