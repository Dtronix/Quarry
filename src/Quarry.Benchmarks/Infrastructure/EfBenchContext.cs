using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Quarry.Benchmarks.Infrastructure;

public class EfBenchContext : DbContext
{
    private readonly SqliteConnection _connection;

    public EfBenchContext(SqliteConnection connection)
    {
        _connection = connection;
    }

    public DbSet<EfUser> Users => Set<EfUser>();
    public DbSet<EfOrder> Orders => Set<EfOrder>();
    public DbSet<EfOrderItem> OrderItems => Set<EfOrderItem>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite(_connection);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EfUser>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.UserId);
            e.Property(u => u.UserName).HasMaxLength(100);
        });

        modelBuilder.Entity<EfOrder>(e =>
        {
            e.ToTable("orders");
            e.HasKey(o => o.OrderId);
            e.Property(o => o.Total).HasPrecision(18, 2);
            e.HasOne(o => o.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.UserId);
        });

        modelBuilder.Entity<EfOrderItem>(e =>
        {
            e.ToTable("order_items");
            e.HasKey(i => i.OrderItemId);
            e.Property(i => i.UnitPrice).HasPrecision(18, 2);
            e.Property(i => i.LineTotal).HasPrecision(18, 2);
            e.Property(i => i.ProductName).HasMaxLength(200);
            e.HasOne(i => i.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(i => i.OrderId);
        });
    }
}
