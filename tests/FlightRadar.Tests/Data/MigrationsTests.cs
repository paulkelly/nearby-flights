using FlightRadar.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FlightRadar.Tests.Data;

public sealed class ModelTests
{
    [Fact]
    public void Model_Always_HasAMigrationForEveryChange()
    {
        // Fails when an entity or OnModelCreating changed but nobody ran `dotnet ef migrations add`: the app would
        // expect columns the database doesn't have. Compares the model with the latest migration's snapshot, so
        // it needs no database (the design-time factory's placeholder connection is never opened).
        using var context = new DesignTimeAppDbContextFactory().CreateDbContext([]);

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The model has changes with no migration. Run: dotnet ef migrations add <Name> " +
            "--project src/FlightRadar.Api --output-dir Data/Migrations");
    }
}

[Trait("Category", "Database")]
[Collection(PostgresCollection.Name)]
public sealed class MigrationsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrate_EmptyDatabase_AppliesEveryMigration()
    {
        await using var context = PostgresFixture.CreateContext(await postgres.CreateDatabaseAsync());

        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0, await context.Owners.CountAsync()); // the table exists and can be queried
    }

    [Fact]
    public async Task Migrate_AlreadyUpToDate_DoesNothing()
    {
        // The `migrate` container runs on every `docker compose up`, so running it twice must be harmless.
        string connectionString = await postgres.CreateDatabaseAsync();
        await using (var first = PostgresFixture.CreateContext(connectionString))
        {
            await first.Database.MigrateAsync();
        }

        await using var second = PostgresFixture.CreateContext(connectionString);
        await second.Database.MigrateAsync();

        Assert.Empty(await second.Database.GetPendingMigrationsAsync());
    }
}
