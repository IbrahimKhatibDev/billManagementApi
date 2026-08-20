using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// Turning the five aging buckets into one bar. The interesting part is not the
/// division — it is that the five rows must survive it, because the strip has a
/// legend and a legend that grows and shrinks as buckets empty is unreadable
/// across two loads.
/// </summary>
public sealed class StackedStripTests
{
    private static AgingBucket Bucket(string label, int count, decimal amount) =>
        new() { Label = label, Count = count, Amount = amount };

    [Fact]
    public void Shares_are_of_money_and_add_up_to_the_whole_bar()
    {
        var segments = StackedStrip.FromAging(new[]
        {
            Bucket("Not yet due", 2, 250m),
            Bucket("1–30 days late", 1, 750m),
        });

        Assert.Equal(25d, segments[0].Percent);
        Assert.Equal(75d, segments[1].Percent);
        Assert.Equal(100d, segments.Sum(s => s.Percent));
    }

    [Fact]
    public void Every_bucket_comes_back_even_the_empty_ones()
    {
        // The API's contract is always five buckets in a fixed order. The strip
        // hides zero-width segments; the legend does not, and both read the same
        // list.
        var segments = StackedStrip.FromAging(new[]
        {
            Bucket("Not yet due", 1, 100m),
            Bucket("1–30 days late", 0, 0m),
            Bucket("31–60 days late", 0, 0m),
            Bucket("61–90 days late", 0, 0m),
            Bucket("Over 90 days late", 0, 0m),
        });

        Assert.Equal(5, segments.Count);
        Assert.Equal("Over 90 days late", segments[4].Label);
        Assert.Equal(0d, segments[4].Percent);
    }

    [Fact]
    public void An_account_owing_nothing_draws_nothing_rather_than_dividing_by_zero()
    {
        // Reachable the moment every bill is paid, which is the state the app is
        // trying to get the user to.
        var segments = StackedStrip.FromAging(new[]
        {
            Bucket("Not yet due", 0, 0m),
            Bucket("1–30 days late", 0, 0m),
        });

        Assert.All(segments, s => Assert.Equal(0d, s.Percent));
    }

    [Fact]
    public void One_bucket_holding_everything_fills_the_bar()
    {
        var segments = StackedStrip.FromAging(new[] { Bucket("Over 90 days late", 3, 640.25m) });

        Assert.Equal(100d, segments[0].Percent);
    }

    [Fact]
    public void A_third_of_an_odd_amount_still_adds_to_a_hundred()
    {
        // The three shares are 33.333…% each. Summed in decimal before the cast
        // they close on 100; summed as three rounded doubles they would not, and
        // the bar would end a hair short of its own container.
        var segments = StackedStrip.FromAging(new[]
        {
            Bucket("a", 1, 33.33m),
            Bucket("b", 1, 33.33m),
            Bucket("c", 1, 33.34m),
        });

        Assert.Equal(100d, segments.Sum(s => s.Percent), 10);
    }

    [Fact]
    public void No_buckets_is_an_empty_strip_and_not_a_crash()
    {
        Assert.Empty(StackedStrip.FromAging(Array.Empty<AgingBucket>()));
    }
}
