using BillsMinimalApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BillsMinimalApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Bill> Bills => Set<Bill>();

    // Last line of defence for the Npgsql "timestamp with time zone" rule — see
    // UtcDateTime for what the rule is and why Unspecified is re-stamped rather
    // than converted. The mappers normalise on the way in, so by the time an
    // entity reaches here it is usually already UTC; this catches anything that
    // reaches the DbContext by another route (the seeder, or a future call site).
    //
    // Reading back always re-stamps Kind=Utc: Npgsql hands us Kind=Local for
    // timestamptz columns, which would otherwise serialise with the host's
    // offset instead of Z.
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        v => UtcDateTime.Normalize(v),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter = new(
        v => UtcDateTime.Normalize(v),
        v => v.HasValue ? (DateTime?)DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

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

        bill.Property(b => b.DueDate).HasConversion(UtcConverter);
        bill.Property(b => b.CreateTime).HasConversion(UtcConverter);
        bill.Property(b => b.UpdateTime).HasConversion(NullableUtcConverter);
    }

    // Every endpoint calls SaveChangesAsync, so overriding only the synchronous
    // SaveChanges() meant the audit fields were never stamped in practice. These
    // two overloads are the roots that all other SaveChanges/SaveChangesAsync
    // overloads funnel through.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampAuditFields()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Bill>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreateTime = now;
                // Version is server-owned: a new row always starts at 1,
                // whatever the client sent.
                entry.Entity.Version = 1;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdateTime = now;
            }
        }
    }
}
