using BillsMinimalApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BillsMinimalApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Bill> Bills => Set<Bill>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var bill = modelBuilder.Entity<Bill>();

        bill.Property(b => b.PaymentDue)
            .HasColumnType("decimal(18,2)");

        bill.Property(b => b.PayeeName)
            .IsRequired()
            .HasMaxLength(255);

        bill.Property(b => b.Version)
            .IsConcurrencyToken();

        bill.Property(b => b.CreateTime)
            .IsRequired();
    }

    public override int SaveChanges()
    {
        var entries = ChangeTracker.Entries<Bill>();
        var now = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreateTime = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdateTime = now;
            }
        }

        return base.SaveChanges();
    }
}
