using Microsoft.EntityFrameworkCore;

namespace Helgrind.Data;

public sealed class HelgrindDbContext(DbContextOptions<HelgrindDbContext> options) : DbContext(options)
{
    public DbSet<ProxyRouteEntity> Routes => Set<ProxyRouteEntity>();

    public DbSet<ProxyClusterEntity> Clusters => Set<ProxyClusterEntity>();

    public DbSet<ProxyDestinationEntity> Destinations => Set<ProxyDestinationEntity>();

    public DbSet<AppSettingsEntity> AppSettings => Set<AppSettingsEntity>();

    public DbSet<StoredCertificateEntity> Certificates => Set<StoredCertificateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProxyClusterEntity>()
            .HasMany(cluster => cluster.Destinations)
            .WithOne(destination => destination.Cluster)
            .HasForeignKey(destination => destination.ClusterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AppSettingsEntity>().HasData(new AppSettingsEntity { Id = 1 });
    }
}