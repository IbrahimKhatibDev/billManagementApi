using BillsMinimalApi.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BillsMinimalApi.Data;

/// <summary>
/// Lets <c>dotnet ef</c> build an <see cref="AppDbContext"/> without executing
/// <c>Program.cs</c>. Without this, the tooling runs the host up to
/// <c>builder.Build()</c> — which now performs startup migration and seeding —
/// and fails with an unhelpful error when no database is reachable.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=billdb;Username=bills;Password=bills_dev_password";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        // NoCurrentUser because there is no request here. The ownership query
        // filter is part of the model either way, but scaffolding a migration
        // only reads the model's shape — it never runs a query through the
        // filter — so "nobody" is the honest answer rather than a limitation.
        return new AppDbContext(options, new NoCurrentUser());
    }
}
