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

    // Npgsql maps DateTime to "timestamp with time zone" and throws on any value
    // whose Kind is not Utc. Values reach us with Kind=Unspecified (JSON dates
    // such as "2026-03-15", and the Blazor <input type="date"> binding) or
    // Kind=Local (Bogus). Normalising once here keeps every call site honest.
    //
    // Unspecified is treated as *already* UTC rather than converted from local
    // time: a date-only payload has no timezone, so ToUniversalTime() would shift
    // it by the host offset and give different results on a developer Mac than in
    // the (UTC) container. Reading back always re-stamps Kind=Utc so round-trips
    // through the API are stable.
    // Written with conditionals rather than a switch expression on purpose:
    // ValueConverter takes an Expression<Func<,>>, and expression trees cannot
    // contain switch expressions (CS8514).
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        v => v.Kind == DateTimeKind.Utc
            ? v
            : v.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(v, DateTimeKind.Utc)
                : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter = new(
        v => v.HasValue
            ? (DateTime?)(v.Value.Kind == DateTimeKind.Utc
                ? v.Value
                : v.Value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                    : v.Value.ToUniversalTime())
            : null,
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
