using FlightRadar.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FlightRadar.Tests.Data;

/// <summary>
/// One real, throwaway Postgres server (a Docker container started by Testcontainers) shared by every test class
/// in the "Postgres" collection, so the container starts once per test run, not once per test. Each test gets its
/// own empty database on it (<see cref="CreateDatabaseAsync"/>), so tests can't see each other's rows.
/// </summary>
/// <remarks>
/// Needs Docker running. Without it, these tests fail at startup; skip them with
/// <c>dotnet test --filter "Category!=Database"</c>. Every test class using this carries
/// <c>[Trait("Category", "Database")]</c> for that filter.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    // The same image as compose.yaml, so tests run against the version the app does.
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public Task InitializeAsync() => container.StartAsync();

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    /// <summary>Creates a new, empty database and returns a connection string for it.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        string name = $"test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(container.GetConnectionString()))
        {
            await connection.OpenAsync();
            // Names can't be query parameters; this one is ours (letters, digits, underscores), never user input.
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    /// <summary>A new database with every migration applied, as the `migrate` container leaves it.</summary>
    public async Task<string> CreateMigratedDatabaseAsync()
    {
        string connectionString = await CreateDatabaseAsync();
        await using var context = CreateContext(connectionString);
        await context.Database.MigrateAsync();
        return connectionString;
    }

    /// <summary>A context on the given database, configured exactly as the app configures its own.</summary>
    public static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseServiceCollectionExtensions.Configure(options, connectionString);
        return new AppDbContext(options.Options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
