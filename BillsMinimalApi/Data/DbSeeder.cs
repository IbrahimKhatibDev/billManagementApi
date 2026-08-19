using BillsMinimalApi.Models;
using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BillsMinimalApi.Data;

public static class DbSeeder
{
    /// <summary>
    /// The account a reviewer logs into. Published in the README on purpose:
    /// a deployed demo nobody can get into is a link, not a demo. It holds
    /// nothing but generated data, and it is an ordinary account with no
    /// elevated rights — the worst anyone can do with it is edit fake bills.
    /// </summary>
    public const string DemoEmail = "demo@billsapp.dev";

    public const string DemoPassword = "Demo12345";

    public static async Task SeedAsync(AppDbContext db, UserManager<AppUser> users)
    {
        var demo = await users.FindByEmailAsync(DemoEmail);

        if (demo is null)
        {
            demo = new AppUser
            {
                UserName = DemoEmail,
                Email = DemoEmail,
                // There is no mail server in this stack and nothing checks the
                // flag, but leaving it false would be a lie in the one table
                // where the answer is knowable.
                EmailConfirmed = true,
            };

            var created = await users.CreateAsync(demo, DemoPassword);

            if (!created.Succeeded)
            {
                // Worth failing startup over: without this account the seeded
                // bills have no owner to belong to, and the documented
                // credentials do not work.
                throw new InvalidOperationException(
                    "Could not create the demo user: "
                    + string.Join("; ", created.Errors.Select(e => e.Description)));
            }
        }

        // The one account in this app that must not be lockable.
        //
        // Everywhere else, five wrong passwords and a fifteen-minute pause is
        // exactly right. Here the password is printed in the README, so there is
        // nothing to guess and nothing to protect — all the lockout can do is
        // hand any visitor a fifteen-minute kill switch on the front door of the
        // demo, and a reviewer who arrives during someone else's joke reads it
        // as an app that does not work.
        //
        // Only this account: LockoutEnabled is per user, and UserManager checks
        // it before it looks at the failure count, so every registered account
        // keeps the protection.
        //
        // Set on every boot rather than in the object initializer above, because
        // CreateAsync overwrites the flag from Lockout.AllowedForNewUsers — and
        // because a database that predates this line needs repairing rather than
        // leaving locked.
        if (demo.LockoutEnabled)
        {
            demo.LockoutEnabled = false;
            await users.UpdateAsync(demo);
        }

        // IgnoreQueryFilters, and scoped to the demo user by hand.
        //
        // Two things are going on. The ownership filter reads ICurrentUser,
        // which is null here because startup is not a request — so without
        // IgnoreQueryFilters this query matches nothing, is therefore always
        // false, and reseeds 25 bills on every single boot. And once the filter
        // is off, "are there any bills" is the wrong question: a database with
        // one registered user's bills in it would leave the demo account empty.
        if (await db.Bills.IgnoreQueryFilters().AnyAsync(b => b.OwnerId == demo.Id))
        {
            return;
        }

        // CreateTime and Version are stamped by AppDbContext.StampAuditFields,
        // so they are deliberately absent here. Due dates are spread across the
        // last five months and the next one so the dashboard's monthly chart and
        // the table's pager both have real data to work with.
        //
        // OwnerId is not stamped: StampAuditFields takes it from the current
        // user, and there is not one. This is the call site that assignment is
        // written to leave alone.
        var bills = new Faker<Bill>()
            .RuleFor(b => b.OwnerId, _ => demo.Id)
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
