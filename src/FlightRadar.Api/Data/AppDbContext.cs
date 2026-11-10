using FlightRadar.Api.Owners;
using Microsoft.EntityFrameworkCore;

namespace FlightRadar.Api.Data;

/// <summary>
/// The app's Postgres database: the things users keep (saved locations), as opposed to Redis, which only caches.
/// </summary>
/// <remarks>
/// A DbContext is a short-lived unit of work: one per request (registered as scoped), tracking the entities it
/// loads and writing all changes in one transaction on <c>SaveChangesAsync</c>. Never share one between requests
/// or threads.
/// <para>
/// The schema lives in <c>Data/Migrations</c>: after changing the model here, run
/// <c>dotnet ef migrations add &lt;Name&gt; --project src/FlightRadar.Api --output-dir Data/Migrations</c> and
/// review the generated file before committing it.
/// </para>
/// </remarks>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Owner> Owners => Set<Owner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Owner>(owner =>
        {
            // Looked up by token hash on every authenticated request, and no two owners may share one.
            owner.HasIndex(o => o.TokenHash).IsUnique();
            owner.Property(o => o.TokenHash).HasMaxLength(32); // SHA-256
        });
    }
}
