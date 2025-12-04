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

        var faker = new Faker("en");

        var bills = new Faker<Bill>()
            .RuleFor(b => b.PayeeName, f => f.Company.CompanyName())
            .RuleFor(b => b.DueDate, f => f.Date.Soon(30))
            .RuleFor(b => b.PaymentDue, f => f.Random.Decimal(10, 500))
            .RuleFor(b => b.Paid, f => f.Random.Bool())
            .RuleFor(b => b.Version, 1)
            .RuleFor(b => b.CreateTime, f => f.Date.Past());

        var fakeBills = bills.Generate(10);

        db.Bills.AddRange(fakeBills);
        await db.SaveChangesAsync();
    }
}
