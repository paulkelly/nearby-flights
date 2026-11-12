using FlightRadar.Api.Devices;
using FlightRadar.Api.Locations;
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
    public DbSet<SavedLocation> SavedLocations => Set<SavedLocation>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceWatch> DeviceWatches => Set<DeviceWatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Owner>(owner =>
        {
            // Looked up by token hash on every authenticated request, and no two owners may share one.
            owner.HasIndex(o => o.TokenHash).IsUnique();
            owner.Property(o => o.TokenHash).HasMaxLength(32); // SHA-256
        });

        modelBuilder.Entity<SavedLocation>(location =>
        {
            location.Property(l => l.Name).HasMaxLength(SavedLocationRules.MaxNameLength);

            // A foreign key: Postgres itself refuses a location whose owner doesn't exist, and deleting an owner
            // deletes their locations with them (cascade), so none are ever left behind belonging to nobody.
            location.HasOne<Owner>().WithMany().HasForeignKey(l => l.OwnerId).OnDelete(DeleteBehavior.Cascade);

            // One "Home" per owner. Enforced by the database, so two saves racing each other can't both win. Also
            // serves "this owner's locations" queries: an index on (owner_id, name) covers lookups by owner_id alone.
            location.HasIndex(l => new { l.OwnerId, l.Name }).IsUnique();
        });

        modelBuilder.Entity<Device>(device =>
        {
            // The client makes the ID (its installation ID), so the database mustn't generate one.
            device.Property(d => d.Id).ValueGeneratedNever();
            device.HasOne<Owner>().WithMany().HasForeignKey(d => d.OwnerId).OnDelete(DeleteBehavior.Cascade);
            device.HasIndex(d => d.OwnerId);
            device.Property(d => d.PushToken).HasMaxLength(DeviceEndpoints.MaxPushTokenLength);
        });

        modelBuilder.Entity<DeviceWatch>(watch =>
        {
            watch.HasKey(w => new { w.DeviceId, w.LocationId });
            // Both cascade: removing a device, or deleting the location in the app, ends the watch with no clean-up code.
            watch.HasOne<Device>().WithMany().HasForeignKey(w => w.DeviceId).OnDelete(DeleteBehavior.Cascade);
            watch.HasOne<SavedLocation>().WithMany().HasForeignKey(w => w.LocationId).OnDelete(DeleteBehavior.Cascade);
            watch.HasIndex(w => w.LocationId); // so deleting a location finds its watches without a full scan
        });
    }
}
