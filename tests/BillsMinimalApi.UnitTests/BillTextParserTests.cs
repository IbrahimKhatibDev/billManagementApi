using BillsMinimalApi.Contracts;
using BillsMinimalApi.Parsing;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// The grammar behind "Add a bill in words". The parser takes today as an
/// argument rather than reading a clock, which is the whole reason a weekday
/// like "fri" can be asserted on at all.
/// </summary>
public sealed class BillTextParserTests
{
    // A Wednesday. Every case below is relative to it.
    private static readonly DateTime Today = new(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_payee_an_amount_and_a_weekday_come_out_of_one_line()
    {
        var parsed = BillTextParser.Parse("Verizon 89.20 fri", Today);

        Assert.Equal("Verizon", parsed.Payee);
        Assert.Equal(89.20m, parsed.Amount);
        Assert.Equal(new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc), parsed.DueDate);
        Assert.Equal(ParseConfidence.High, parsed.Confidence);
    }

    [Fact]
    public void A_weekday_points_forward_and_never_at_the_day_you_are_standing_on()
    {
        // "wed" typed on a Wednesday means the one coming. Resolving it to today
        // would silently file the bill as due now.
        var parsed = BillTextParser.Parse("Rent 1200 wed", Today);

        Assert.Equal(new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc), parsed.DueDate);
    }

    [Fact]
    public void A_slashed_date_is_not_mistaken_for_the_amount()
    {
        // The amount is the first number token, and 8/21 is full of number
        // tokens. The pattern refuses digits that touch a slash.
        var parsed = BillTextParser.Parse("Verizon 89.20 8/21", Today);

        Assert.Equal(89.20m, parsed.Amount);
        Assert.Equal(new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc), parsed.DueDate);
    }

    [Fact]
    public void A_month_and_day_already_gone_this_year_lands_in_the_next_one()
    {
        var parsed = BillTextParser.Parse("Insurance 300 mar 3", Today);

        Assert.Equal(new DateTime(2027, 3, 3, 0, 0, 0, DateTimeKind.Utc), parsed.DueDate);
    }

    [Fact]
    public void A_preposition_carries_no_information_and_is_dropped()
    {
        var parsed = BillTextParser.Parse("Water 42.10 due tomorrow", Today);

        Assert.Equal("Water", parsed.Payee);
        Assert.Equal(new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc), parsed.DueDate);
    }

    [Fact]
    public void A_dollar_sign_belongs_to_the_amount_and_not_to_the_payee()
    {
        var parsed = BillTextParser.Parse("Coffee Shop $12.50 today", Today);

        Assert.Equal("Coffee Shop", parsed.Payee);
        Assert.Equal(12.50m, parsed.Amount);
        Assert.Equal(Today, parsed.DueDate);
    }

    [Fact]
    public void A_line_with_no_date_reads_low_rather_than_guessing_one()
    {
        // Low confidence is the signal the client uses to keep the preview open
        // for correction instead of offering a one-click save.
        var parsed = BillTextParser.Parse("Verizon 89.20", Today);

        Assert.Equal("Verizon", parsed.Payee);
        Assert.Equal(89.20m, parsed.Amount);
        Assert.Null(parsed.DueDate);
        Assert.Equal(ParseConfidence.Low, parsed.Confidence);
    }

    [Fact]
    public void An_impossible_date_is_no_date_rather_than_an_exception()
    {
        // The user is typing; every intermediate string reaches this method.
        var parsed = BillTextParser.Parse("Gym 30 2/30", Today);

        Assert.Null(parsed.DueDate);
        Assert.Equal(ParseConfidence.Low, parsed.Confidence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_typed_yet_parses_to_nothing(string? text)
    {
        var parsed = BillTextParser.Parse(text, Today);

        Assert.Null(parsed.Payee);
        Assert.Null(parsed.Amount);
        Assert.Null(parsed.DueDate);
        Assert.Equal(ParseConfidence.Low, parsed.Confidence);
    }

    [Fact]
    public void The_due_date_is_UTC_because_Npgsql_rejects_anything_else()
    {
        var parsed = BillTextParser.Parse("Verizon 89.20 fri", Today);

        Assert.Equal(DateTimeKind.Utc, parsed.DueDate!.Value.Kind);
    }

    [Fact]
    public void A_day_before_the_month_name_is_also_a_valid_date()
    {
        var parsed = BillTextParser.Parse("Verizon 89.20 21 aug", Today);

        Assert.Equal("Verizon", parsed.Payee);
        Assert.Equal(89.20m, parsed.Amount);
        Assert.Equal(new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc), parsed.DueDate);
        Assert.Equal(ParseConfidence.High, parsed.Confidence);
    }

    [Theory]
    [InlineData("Verizon 89.20 8/21/26")]
    [InlineData("Verizon 89.20 8/21/2026")]
    public void A_two_digit_or_four_digit_year_resolves_to_the_same_date(string text)
    {
        var parsed = BillTextParser.Parse(text, Today);

        Assert.Equal(new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc), parsed.DueDate);
    }

    [Fact]
    public void An_explicit_past_year_is_kept_rather_than_rolled_forward()
    {
        // Only a year the caller did not type is eligible to roll into next
        // year; "8/21/20" means 2020, not a typo for a future one.
        var parsed = BillTextParser.Parse("Verizon 89.20 8/21/20", Today);

        Assert.Equal(new DateTime(2020, 8, 21, 0, 0, 0, DateTimeKind.Utc), parsed.DueDate);
    }

    [Theory]
    [InlineData("Verizon 89.20 friday", 2026, 8, 21)]
    [InlineData("Verizon 89.20 tmrw", 2026, 8, 20)]
    public void A_full_weekday_name_and_the_tmrw_alias_resolve_like_their_short_forms(
        string text, int year, int month, int day)
    {
        var parsed = BillTextParser.Parse(text, Today);

        Assert.Equal(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc), parsed.DueDate);
    }

    [Theory]
    [InlineData("Verizon 89.20 aug 18 2027")]
    [InlineData("Verizon 89.20 Aug 18, 2027")]
    [InlineData("Verizon 89.20 18 aug 2027")]
    [InlineData("Verizon 89.20 aug 18 27")]
    public void A_spelled_out_month_takes_a_year_the_way_the_slashed_form_does(string text)
    {
        // "8/21/2027" has always worked and "aug 18 2027" came back with no date
        // at all — the same information typed the way most people write it, read
        // as unparseable. The comma is optional because a date written out long
        // usually has one.
        var parsed = BillTextParser.Parse(text, Today);

        Assert.Equal(new DateTime(2027, 8, 18, 0, 0, 0, DateTimeKind.Utc), parsed.DueDate);
        Assert.Equal(ParseConfidence.High, parsed.Confidence);
    }

    [Fact]
    public void A_spelled_out_month_in_a_past_year_is_kept_rather_than_rolled_forward()
    {
        // Same rule the slashed form follows: only a year the caller left out is
        // eligible to roll. Without it "aug 18 2020" would file as 2020 — which
        // is already past, so the roll-forward would quietly make it 2021.
        var parsed = BillTextParser.Parse("Verizon 89.20 aug 18 2020", Today);

        Assert.Equal(new DateTime(2020, 8, 18, 0, 0, 0, DateTimeKind.Utc), parsed.DueDate);
    }

    [Fact]
    public void A_trailing_period_after_the_month_name_is_optional()
    {
        var parsed = BillTextParser.Parse("Verizon 89.20 Aug. 21", Today);

        Assert.Equal(new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc), parsed.DueDate);
    }

    [Theory]
    [InlineData("Verizon 1,299.50 fri", 1299.50)]
    [InlineData("Verizon 1,299 fri", 1299)]
    [InlineData("Verizon 12,345,678.90 fri", 12345678.90)]
    public void A_thousands_separator_is_part_of_the_amount(string text, decimal expected)
    {
        // The trailing lookaround excluded digits, dots and slashes but not
        // commas, so "1,299.50" matched the "1" and stopped: a bill for $1,299.50
        // was read as one for $1.00, with no error and a date that no longer
        // parsed. Wrong money, silently.
        var parsed = BillTextParser.Parse(text, Today);

        Assert.Equal(expected, parsed.Amount);
        Assert.Equal("Verizon", parsed.Payee);
        Assert.Equal(new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc), parsed.DueDate);
    }

    [Fact]
    public void A_number_too_large_for_a_decimal_is_a_low_reading_and_not_a_crash()
    {
        // An account number pasted into the box. decimal.Parse threw
        // OverflowException past ~7.9e28 and the parse endpoint has no exception
        // handler, so this came back as a 500 on every debounced keystroke while
        // the chips silently vanished.
        var parsed = BillTextParser.Parse($"Verizon {new string('9', 40)} fri", Today);

        Assert.Null(parsed.Amount);
        Assert.Equal("Verizon", parsed.Payee);
        Assert.Equal(ParseConfidence.Low, parsed.Confidence);
    }

    [Theory]
    [InlineData("Gas 0 fri")]
    [InlineData("Gas 0.00 fri")]
    public void An_amount_the_add_button_would_refuse_is_not_a_confident_reading(string text)
    {
        // ParsedBill documents High as "all three fields resolved", and the
        // client's IsComplete holds the amount to MinimumAmount. Calling this
        // high-confidence put a confident reading next to a dead Add button with
        // nothing to say which chip was at fault.
        var parsed = BillTextParser.Parse(text, Today);

        Assert.Equal(0m, parsed.Amount);
        Assert.NotNull(parsed.DueDate);
        Assert.Equal(ParseConfidence.Low, parsed.Confidence);
    }
}
