using BillsMinimalApi.Auth;
using BillsMinimalApi.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BillsMinimalApi.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    private readonly ICurrentUser _currentUser;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
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
        // Builds the Identity tables. Not optional, and easy to drop when this
        // override already existed for something else.
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

        // 450 is Identity's own key length — the ceiling that lets a string key
        // be indexed under the row-size limits of the providers Identity
        // supports. Matching it keeps the foreign key's types identical on both
        // sides.
        bill.Property(b => b.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        // No navigation property in either direction: nothing in this app ever
        // loads a user through a bill or a bill through a user, and an unused
        // Bill.Owner is one more thing a careless Include could drag over the
        // wire. The relationship exists for the constraint and the cascade.
        bill.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(b => b.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        // OwnerId leads because every query has an equality predicate on it —
        // the global filter below guarantees that — so it is the most selective
        // column available and the only one always present. Paid next because
        // every status filter tests it, then DueDate, which serves both the
        // from/to window and the ORDER BY once the first two are narrowed.
        // "Overdue for this user" — OwnerId = x AND Paid = false AND DueDate <
        // today — is then equality, equality, range: exactly the shape a
        // composite index handles well.
        bill.HasIndex(b => new { b.OwnerId, b.Paid, b.DueDate });

        // OWNERSHIP
        //
        // The one line that makes "user A cannot see user B's bills" a property
        // of the model rather than something every endpoint has to remember. EF
        // appends it to every query against Bills — including the ones inside
        // CountAsync, the GroupBy in BillSummaryBuilder, and the FindAsync in
        // the id routes — so a new endpoint is scoped before it is written.
        //
        // Evaluated per query, not once at startup: the expression closes over
        // the injected service, and EF re-reads the property each time it
        // compiles the query's parameters. Capturing _currentUser.Id into a
        // field here would freeze the first request's user into the model.
        //
        // An anonymous request leaves Id null, and OwnerId is non-nullable, so
        // the comparison matches nothing. Failing closed is the point: the
        // fallback authorization policy should have rejected the request long
        // before it reached here, and if it ever does not, the answer is an
        // empty set rather than everybody's data.
        bill.HasQueryFilter(b => b.OwnerId == _currentUser.Id);

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

                // Ownership comes from the token, not the request body. The
                // seeder runs with no current user and assigns OwnerId itself,
                // which is why this assigns rather than overwrites
                // unconditionally — but a bill with neither is a bug worth
                // failing on rather than a row the query filter would then hide
                // from everybody, including whoever created it.
                if (_currentUser.Id is { } ownerId)
                {
                    entry.Entity.OwnerId = ownerId;
                }

                if (string.IsNullOrEmpty(entry.Entity.OwnerId))
                {
                    throw new InvalidOperationException(
                        "Cannot insert a Bill with no OwnerId: there is no authenticated "
                        + "user on this request and none was assigned explicitly.");
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdateTime = now;

                // Ownership is immutable. Nothing currently writes to OwnerId on
                // an update — BillMapper.ApplyEditableFields does not touch it —
                // so this guards against a future call site rather than a
                // present one, and it costs a line.
                entry.Property(b => b.OwnerId).IsModified = false;
            }
        }
    }
}
