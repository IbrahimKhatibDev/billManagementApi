using System.Globalization;
using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// The one sentence the Overview leads with. It is prose assembled from five
/// figures, and every branch of it is reachable from real data — an account with
/// nothing late, an account with nothing at all, an account with one bill one day
/// overdue.
/// <para>
/// The culture is passed in rather than read from the thread. The app pins en-US
/// in Program.cs, but a unit test has no Program.cs, and a suite that passes only
/// on a machine set to dollars is not a test of the sentence.
/// </para>
/// </summary>
public sealed class ObligationSentenceTests
{
    private static readonly CultureInfo Money = CultureInfo.GetCultureInfo("en-US");

    private static string Describe(BillSummary summary) =>
        ObligationSentence.Describe(summary, Money);

    private static ObligationParts Parts(BillSummary summary) =>
        ObligationSentence.DescribeParts(summary, Money);

    /// <summary>The figures from the design handoff, reused by the split tests
    /// below so they are asserting against the same sentence as the one above.
    /// </summary>
    private static BillSummary Handoff() => new()
    {
        TotalBilled = 6_108.50m,
        PaidAmount = 4_419.52m,
        OverdueAmount = 1_398.99m,
        OverdueCount = 8,
        DueSoonAmount = 289.99m,
        Late = { new SummaryBill { DaysLate = 156 } },
    };

    [Fact]
    public void The_headline_reads_as_the_design_wrote_it()
    {
        // The handoff's own figures, which are cross-checked against the app's
        // Reports screen — so this is the sentence a real account produces.
        Assert.Equal(
            "$1,398.99 of it is already late, spread across 8 bills — the oldest by 156 days. "
            + "The rest, $289.99, falls due inside the next 30 days.",
            Describe(Handoff()));
    }

    [Fact]
    public void The_coloured_clause_is_the_figure_and_the_claim_about_it()
    {
        // Where the Overview stops colouring. The comma belongs to the remainder,
        // so the highlight ends on the word rather than on punctuation.
        Assert.Equal("$1,398.99 of it is already late", Parts(Handoff()).Late);
    }

    [Fact]
    public void The_two_parts_join_back_into_the_sentence_with_nothing_between_them()
    {
        // The component concatenates these across a span boundary. A part that
        // needed a separator would put a space in front of the comma.
        var parts = Parts(Handoff());

        Assert.Equal(Describe(Handoff()), parts.Late + parts.Rest);
    }

    [Fact]
    public void A_settled_account_has_nothing_to_colour()
    {
        var summary = new BillSummary { TotalBilled = 900m, PaidAmount = 900m };

        Assert.Empty(Parts(summary).Late);
        Assert.Equal(Describe(summary), Parts(summary).Rest);
    }

    [Fact]
    public void An_account_with_nothing_late_yet_has_nothing_to_colour_either()
    {
        // Money is outstanding, but all of it is still ahead of its due date, so
        // the sentence never names a late figure.
        var summary = new BillSummary { TotalBilled = 400m, DueSoonAmount = 400m };

        Assert.Empty(Parts(summary).Late);
        Assert.Equal(Describe(summary), Parts(summary).Rest);
    }

    [Fact]
    public void A_settled_account_gets_a_sentence_rather_than_an_empty_one()
    {
        var summary = new BillSummary { TotalBilled = 900m, PaidAmount = 900m };

        Assert.Equal("Nothing outstanding — every bill on the books is paid.", Describe(summary));
    }

    [Fact]
    public void One_late_bill_is_a_bill_and_not_bills()
    {
        var summary = new BillSummary
        {
            TotalBilled = 50m,
            OverdueAmount = 50m,
            OverdueCount = 1,
            Late = { new SummaryBill { DaysLate = 4 } },
        };

        Assert.Contains("spread across 1 bill —", Describe(summary));
    }

    [Fact]
    public void One_day_late_is_a_day_and_not_days()
    {
        var summary = new BillSummary
        {
            TotalBilled = 50m,
            OverdueAmount = 50m,
            OverdueCount = 1,
            Late = { new SummaryBill { DaysLate = 1 } },
        };

        Assert.Contains("the oldest by 1 day.", Describe(summary));
    }

    [Fact]
    public void Nothing_late_says_so_instead_of_naming_a_zero()
    {
        var summary = new BillSummary { TotalBilled = 400m, DueSoonAmount = 400m };

        Assert.Equal(
            "None of it is late. $400.00 falls due inside the next 30 days.",
            Describe(summary));
    }

    [Fact]
    public void Nothing_due_soon_says_so_too()
    {
        // Everything outstanding sits further out than the window. The sentence
        // still has to end, and "and nothing is coming up either" is the news.
        var summary = new BillSummary { TotalBilled = 400m };

        Assert.Equal(
            "None of it is late. Nothing falls due inside the next 30 days.",
            Describe(summary));
    }

    [Fact]
    public void Money_outside_both_windows_is_never_called_the_rest()
    {
        // $1,000 outstanding, $200 late, $300 due inside 30 days — and $500 due
        // later. Calling the $300 "the rest" would be a lie the reader can check
        // against the total printed directly above it.
        var summary = new BillSummary
        {
            TotalBilled = 1_000m,
            OverdueAmount = 200m,
            OverdueCount = 2,
            DueSoonAmount = 300m,
            Late = { new SummaryBill { DaysLate = 9 } },
        };

        Assert.Equal(
            "$200.00 of it is already late, spread across 2 bills — the oldest by 9 days. "
            + "$300.00 of the remainder falls due inside the next 30 days.",
            Describe(summary));
    }

    [Fact]
    public void The_oldest_comes_from_the_late_list_and_not_from_a_second_figure()
    {
        // Late is ordered oldest first by the builder, so the head of it is the
        // answer. Reading it here rather than sending a separate number is what
        // stops the sentence disagreeing with the list underneath it.
        var summary = new BillSummary
        {
            TotalBilled = 300m,
            OverdueAmount = 300m,
            OverdueCount = 3,
            Late =
            {
                new SummaryBill { DaysLate = 90 },
                new SummaryBill { DaysLate = 12 },
                new SummaryBill { DaysLate = 2 },
            },
        };

        Assert.Contains("the oldest by 90 days.", Describe(summary));
    }
}
