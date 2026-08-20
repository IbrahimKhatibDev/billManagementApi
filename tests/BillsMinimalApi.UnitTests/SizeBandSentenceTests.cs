using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// The one-line replacement for the bill-size distribution table.
/// <para>
/// The fixture is built so the designed sentence falls out exactly: the
/// $250–$499 band holds 12 of 26 bills and $4,180 of $5,500, which is 76% of
/// the money — "Twelve of 26 bills sit between $250 and $499 — that band is
/// 76% of the money."
/// </para>
/// </summary>
public class SizeBandSentenceTests
{
    private static SizeBand Band(string label, int count, decimal total) =>
        new() { Label = label, Count = count, Total = total };

    private static List<SizeBand> Handoff() =>
        new()
        {
            Band("Under $50", 3, 120.00m),
            Band("$50 – $99", 4, 300.00m),
            Band("$100 – $249", 7, 900.00m),
            Band("$250 – $499", 12, 4180.00m),
            Band("$500 and over", 0, 0.00m),
        };

    [Fact]
    public void Describe_matches_the_designed_sentence()
    {
        Assert.Equal(
            "Twelve of 26 bills sit between $250 and $499 — that band is 76% of the money.",
            SizeBandSentence.Describe(Handoff()));
    }

    [Fact]
    public void Describe_picks_the_band_holding_the_most_money_not_the_most_bills()
    {
        // Nine small bills against two large ones: the sentence is about where
        // the money is, so it must name the $500 band despite the head count.
        var sentence = SizeBandSentence.Describe(new List<SizeBand>
        {
            Band("Under $50", 9, 270.00m),
            Band("$500 and over", 2, 1730.00m),
        });

        Assert.Equal(
            "Two of 11 bills sit at $500 or more — that band is 87% of the money.",
            sentence);
    }

    [Fact]
    public void Describe_breaks_a_money_tie_on_the_smaller_band()
    {
        var sentence = SizeBandSentence.Describe(new List<SizeBand>
        {
            Band("Under $50", 8, 400.00m),
            Band("$500 and over", 1, 400.00m),
        });

        Assert.StartsWith("Eight of 9 bills sit under $50", sentence);
    }

    [Fact]
    public void Describe_speaks_differently_when_every_bill_is_in_one_band()
    {
        var sentence = SizeBandSentence.Describe(new List<SizeBand>
        {
            Band("Under $50", 0, 0m),
            Band("$250 – $499", 8, 2600.00m),
        });

        Assert.Equal("All 8 bills sit between $250 and $499.", sentence);
    }

    [Fact]
    public void Describe_speaks_differently_for_a_single_bill()
    {
        var sentence = SizeBandSentence.Describe(new List<SizeBand>
        {
            Band("Under $50", 1, 42.00m),
        });

        Assert.Equal("The only bill in this range is under $50.", sentence);
    }

    [Fact]
    public void Describe_is_absent_for_an_empty_range()
    {
        Assert.Null(SizeBandSentence.Describe(new List<SizeBand>()));
    }

    [Fact]
    public void Describe_is_absent_when_every_band_is_empty()
    {
        var bands = new List<SizeBand>
        {
            Band("Under $50", 0, 0m),
            Band("$500 and over", 0, 0m),
        };

        Assert.Null(SizeBandSentence.Describe(bands));
    }

    [Fact]
    public void Describe_tolerates_null()
    {
        Assert.Null(SizeBandSentence.Describe(null));
    }

    [Theory]
    [InlineData("Under $50", "under $50")]
    [InlineData("$50 – $99", "between $50 and $99")]
    [InlineData("$100 – $249", "between $100 and $249")]
    [InlineData("$250 – $499", "between $250 and $499")]
    [InlineData("$500 and over", "at $500 or more")]
    public void Phrase_reads_each_server_label_as_prose(string label, string expected)
    {
        Assert.Equal(expected, SizeBandSentence.Phrase(label));
    }

    [Theory]
    [InlineData(null, "in that band")]
    [InlineData("", "in that band")]
    [InlineData("   ", "in that band")]
    public void Phrase_falls_back_when_there_is_no_label(string? label, string expected)
    {
        Assert.Equal(expected, SizeBandSentence.Phrase(label));
    }

    [Fact]
    public void Phrase_falls_back_to_the_label_itself_for_a_shape_it_does_not_know()
    {
        // A band reworded server-side should degrade to something readable
        // rather than to a sentence that reads as a bug.
        Assert.Equal("at four figures", SizeBandSentence.Phrase("four figures"));
    }
}
