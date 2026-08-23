using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// The cumulative-share arithmetic behind the "Who you owe" table.
/// <para>
/// The fixture is the handoff's own figures: $1,688.98 outstanding, split so
/// that the top three payees come to $1,047.20 — 62.0% — which is the
/// "Three payees account for 62% of everything you owe." framing the design
/// calls for. The numbers are chosen so the assertion is about the arithmetic
/// and not about a rounding boundary: two payees reach 45%, three reach 62%,
/// and the 60% threshold sits unambiguously between them.
/// </para>
/// </summary>
public class ParetoRowTests
{
    private static PayeeTotals Payee(string name, decimal outstanding, decimal paid = 0m, int bills = 1) =>
        new()
        {
            Payee = name,
            Bills = bills,
            Billed = outstanding + paid,
            Paid = paid,
        };

    private static List<PayeeTotals> Handoff() =>
        new()
        {
            Payee("Daugherty, Larson and Moen", 400.00m),
            Payee("Bergstrom Group", 360.00m),
            Payee("Kuhlman-Rippin", 287.20m),
            Payee("Torphy LLC", 250.00m),
            Payee("Hegmann and Sons", 220.00m),
            Payee("Wisozk Inc", 171.78m),
        };

    [Fact]
    public void Build_orders_by_outstanding_descending()
    {
        var rows = ParetoRows.Build(new List<PayeeTotals>
        {
            Payee("Small", 10m),
            Payee("Large", 300m),
            Payee("Middle", 90m),
        });

        Assert.Equal(new[] { "Large", "Middle", "Small" }, rows.Select(r => r.Payee));
    }

    [Fact]
    public void Build_breaks_ties_on_payee_name()
    {
        var rows = ParetoRows.Build(new List<PayeeTotals>
        {
            Payee("beta", 100m),
            Payee("Alpha", 100m),
        });

        Assert.Equal(new[] { "Alpha", "beta" }, rows.Select(r => r.Payee));
    }

    [Fact]
    public void Build_drops_payees_with_nothing_outstanding()
    {
        var rows = ParetoRows.Build(new List<PayeeTotals>
        {
            Payee("Owes nothing", 0m, paid: 500m),
            Payee("Owes something", 120m, paid: 30m),
        });

        var row = Assert.Single(rows);
        Assert.Equal("Owes something", row.Payee);
    }

    [Fact]
    public void Build_returns_nothing_when_every_bill_is_paid()
    {
        var rows = ParetoRows.Build(new List<PayeeTotals>
        {
            Payee("A", 0m, paid: 400m),
            Payee("B", 0m, paid: 90m),
        });

        Assert.Empty(rows);
    }

    [Fact]
    public void Build_tolerates_null()
    {
        Assert.Empty(ParetoRows.Build(null));
    }

    [Fact]
    public void Build_carries_the_bill_count_through()
    {
        var rows = ParetoRows.Build(new List<PayeeTotals> { Payee("A", 50m, bills: 4) });

        Assert.Equal(4, Assert.Single(rows).Bills);
    }

    [Fact]
    public void Share_percents_sum_to_a_hundred()
    {
        var rows = ParetoRows.Build(Handoff());

        Assert.Equal(100d, rows.Sum(r => r.SharePercent), 6);
    }

    [Fact]
    public void Cumulative_percent_ends_at_a_hundred()
    {
        var rows = ParetoRows.Build(Handoff());

        Assert.Equal(100d, rows[^1].CumulativePercent, 6);
    }

    [Fact]
    public void Cumulative_percent_is_the_running_total_of_the_shares()
    {
        var rows = ParetoRows.Build(Handoff());

        var running = 0d;

        foreach (var row in rows)
        {
            running += row.SharePercent;
            Assert.Equal(running, row.CumulativePercent, 6);
        }
    }

    [Fact]
    public void Cumulative_percent_never_decreases()
    {
        var rows = ParetoRows.Build(Handoff());

        for (var i = 1; i < rows.Count; i++)
        {
            Assert.True(rows[i].CumulativePercent >= rows[i - 1].CumulativePercent);
        }
    }

    [Fact]
    public void Three_payees_reach_the_headline_threshold()
    {
        var rows = ParetoRows.Build(Handoff());

        Assert.Equal(3, ParetoRows.PayeesToReach(rows, ParetoRows.HeadlineThreshold));
    }

    [Fact]
    public void PayeesToReach_counts_nobody_when_there_are_no_rows()
    {
        Assert.Equal(0, ParetoRows.PayeesToReach(new List<ParetoRow>(), 60d));
    }

    [Fact]
    public void PayeesToReach_counts_everybody_when_the_threshold_is_unreachable()
    {
        var rows = ParetoRows.Build(Handoff());

        Assert.Equal(rows.Count, ParetoRows.PayeesToReach(rows, 101d));
    }

    [Fact]
    public void Headline_matches_the_designed_sentence()
    {
        var rows = ParetoRows.Build(Handoff());

        Assert.Equal(
            "Three payees account for 62% of everything you owe.",
            ParetoRows.Headline(rows));
    }

    [Fact]
    public void Headline_is_singular_for_a_single_payee()
    {
        var rows = ParetoRows.Build(new List<PayeeTotals> { Payee("Only", 400m) });

        Assert.Equal(
            "One payee accounts for 100% of everything you owe.",
            ParetoRows.Headline(rows));
    }

    [Fact]
    public void Headline_is_absent_when_nothing_is_owed()
    {
        Assert.Null(ParetoRows.Headline(new List<ParetoRow>()));
    }

    [Theory]
    [InlineData(0, "Zero")]
    [InlineData(1, "One")]
    [InlineData(3, "Three")]
    [InlineData(12, "Twelve")]
    [InlineData(20, "Twenty")]
    public void Spell_writes_small_counts_as_words(int count, string expected)
    {
        Assert.Equal(expected, NumberWords.Spell(count));
    }

    [Theory]
    [InlineData(21, "21")]
    [InlineData(26, "26")]
    [InlineData(-1, "-1")]
    public void Spell_writes_everything_else_as_digits(int count, string expected)
    {
        Assert.Equal(expected, NumberWords.Spell(count));
    }
}
