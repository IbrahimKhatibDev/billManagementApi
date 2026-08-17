using BillsMinimalApi.Models;
using Bogus;
using Microsoft.EntityFrameworkCore;

namespace BillsMinimalApi.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // Only seed when db is empty
        if (await db.Bills.AnyAsync())
            return;

        // CreateTime and Version are stamped by AppDbContext.StampAuditFields,
        // so they are deliberately absent here. Due dates are spread across the
        // last five months and the next one so the dashboard's monthly chart and
        // the table's pager both have real data to work with.
        var bills = new Faker<Bill>()
            .RuleFor(b => b.PayeeName, f => f.Company.CompanyName())
            .RuleFor(b => b.DueDate, f => f.Date.Between(
                DateTime.UtcNow.AddMonths(-5),
                DateTime.UtcNow.AddMonths(1)))
            .RuleFor(b => b.PaymentDue, f => Math.Round(f.Random.Decimal(10, 500), 2))
            .RuleFor(b => b.Paid, f => f.Random.Bool());

        var fakeBills = bills.Generate(25);

        db.Bills.AddRange(fakeBills);
        await db.SaveChangesAsync();
    }
}
