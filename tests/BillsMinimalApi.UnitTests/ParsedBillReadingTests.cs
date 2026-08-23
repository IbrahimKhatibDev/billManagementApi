using System.Globalization;
using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// Whether a parse is finished enough to post, and how each piece of it reads.
/// <para>
/// The parser answers "what did I find"; this answers "is that a bill yet". They
/// are different questions — a line with a payee and an amount but no date parses
/// fine and cannot be saved.
/// </para>
/// </summary>
public sealed class ParsedBillReadingTests
{
    // Pinned rather than current-culture: "C" formats differently on every
    // machine, and a test that passes only where it was written is not a test.
    private static readonly CultureInfo Formats = CultureInfo.GetCultureInfo("en-US");

    private static ParsedBill Reading(
        string? payee = "Verizon",
        decimal? amount = 89.20m,
        DateTime? due = null) =>
        new()
        {
            Payee = payee,
            Amount = amount,
            DueDate = due ?? new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc),
            Confidence = ParseConfidence.High,
        };

    [Fact]
    public void All_three_pieces_make_a_bill()
    {
        Assert.True(ParsedBillReading.IsComplete(Reading()));
    }

    [Fact]
    public void Nothing_typed_yet_is_not_a_bill()
    {
        Assert.False(ParsedBillReading.IsComplete(null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_reading_with_no_payee_is_not_a_bill(string? payee)
    {
        Assert.False(ParsedBillReading.IsComplete(Reading(payee: payee)));
    }

    [Fact]
    public void A_reading_with_no_amount_is_not_a_bill()
    {
        Assert.False(ParsedBillReading.IsComplete(Reading(amount: null)));
    }

    [Fact]
    public void A_reading_with_no_date_is_not_a_bill()
    {
        // The parser is happy to return this — "Verizon 89.20" with no date
        // token at all reads as low confidence, not as an error.
        var reading = new ParsedBill { Payee = "Verizon", Amount = 89.20m };

        Assert.False(ParsedBillReading.IsComplete(reading));
    }

    [Fact]
    public void An_amount_the_api_would_refuse_is_not_a_bill()
    {
        // "Verizon 0 fri" parses: the amount pattern takes the first number it
        // finds, and zero is a number. The API's floor is 0.01.
        Assert.False(ParsedBillReading.IsComplete(Reading(amount: 0m)));
    }

    [Fact]
    public void The_payee_reads_back_trimmed()
    {
        Assert.Equal("Verizon", ParsedBillReading.PayeeText(Reading(payee: "  Verizon  ")));
    }

    [Fact]
    public void A_missing_payee_asks_for_one()
    {
        Assert.Equal("add a payee", ParsedBillReading.PayeeText(Reading(payee: null)));
        Assert.Equal("add a payee", ParsedBillReading.PayeeText(null));
    }

    [Fact]
    public void The_amount_reads_as_money()
    {
        Assert.Equal("$89.20", ParsedBillReading.AmountText(Reading(), Formats));
    }

    [Fact]
    public void A_missing_amount_asks_for_one()
    {
        Assert.Equal("add an amount", ParsedBillReading.AmountText(Reading(amount: null), Formats));
        Assert.Equal("add an amount", ParsedBillReading.AmountText(null, Formats));
    }

    [Fact]
    public void The_date_reads_as_a_day()
    {
        Assert.Equal("Aug 21, 2026", ParsedBillReading.DueText(Reading(), Formats));
    }

    [Fact]
    public void A_missing_date_asks_for_one()
    {
        var reading = new ParsedBill { Payee = "Verizon", Amount = 89.20m };

        Assert.Equal("add a date", ParsedBillReading.DueText(reading, Formats));
        Assert.Equal("add a date", ParsedBillReading.DueText(null, Formats));
    }
}
