using FlightRadar.Api.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace FlightRadar.Api.Data;

public static class DatabaseServiceCollectionExtensions
{
    public const string ConnectionStringName = "Postgres";

    /// <summary>
    /// How long to wait for a connection to Postgres, in seconds (Npgsql's default is 15). If the database is
    /// down, requests and health checks should find out quickly: Docker's health probe gives up after 5 s.
    /// </summary>
    private const int ConnectTimeoutSeconds = 3;

    /// <summary>
    /// If "ConnectionStrings:Postgres" is set, registers <see cref="AppDbContext"/> and its health check. If not,
    /// nothing is registered and the app runs without saved locations (flights work exactly as before), like
    /// Redis being optional: Rider runs and tests don't need a database.
    /// </summary>
    public static IServiceCollection AddOptionalDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        services.AddDbContext<AppDbContext>(options => Configure(options, connectionString));

        // Part of /health/ready. Degraded, not Unhealthy, for the same reason as Redis: without the database the
        // API still serves flights, so it shouldn't be taken out of service. The check opens a connection
        // (CanConnectAsync), so it fails within ConnectTimeoutSeconds.
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("postgres", failureStatus: HealthStatus.Degraded, tags: [HealthTags.Ready]);

        return services;
    }

    /// <summary>Shared by the app, the design-time factory (dotnet ef) and the tests, so all build the same model.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = ConnectTimeoutSeconds,
            // Npgsql first offers Kerberos (GSSAPI) encryption, a corporate single-sign-on feature we don't use. The
            // chiseled image has no Kerberos library, so every new connection logged "Error: libgssapi_krb5.so.2:
            // cannot open shared object file" before carrying on without it. Not asking avoids the noise.
            GssEncryptionMode = GssEncryptionMode.Disable,
        };

        options
            .UseNpgsql(builder.ConnectionString)
            // Tables and columns as saved_locations / created_at, Postgres's own convention. EF Core's default
            // (SavedLocations, CreatedAt) works, but Postgres folds unquoted names to lower case, so every hand-
            // written query in psql would need "Quotes" around every name.
            .UseSnakeCaseNamingConvention();
    }
}
