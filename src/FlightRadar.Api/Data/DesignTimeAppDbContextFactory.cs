using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlightRadar.Api.Data;

/// <summary>
/// How the EF Core tools (<c>dotnet ef</c>, and the migration bundle the <c>migrate</c> container runs) create an
/// <see cref="AppDbContext"/>. Without this they would start the whole app (Program.cs) just to get one, needing
/// every setting the app needs. This reads only the connection string: from user-secrets on a developer's machine,
/// or the <c>ConnectionStrings__Postgres</c> environment variable (Docker).
/// </summary>
/// <remarks>
/// <c>dotnet ef migrations add</c> doesn't connect, so it works without a connection string. Commands that do
/// (<c>database update</c>) then fail on the placeholder host, whose name says what's wrong.
/// </remarks>
public sealed class DesignTimeAppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<AppDbContext>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString(DatabaseServiceCollectionExtensions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=connection-string-not-set.invalid";
        }

        var options = new DbContextOptionsBuilder<AppDbContext>();
        DatabaseServiceCollectionExtensions.Configure(options, connectionString);
        return new AppDbContext(options.Options);
    }
}
