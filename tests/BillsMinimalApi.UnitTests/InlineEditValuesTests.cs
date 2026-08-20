using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// Turning what someone typed into a row into a value the API will accept.
/// <para>
/// The stakes are higher than they look. These values go straight into a PUT
/// with no form validation in front of them, and the due date goes on to Npgsql,
/// which throws on a <c>DateTime</c> that is not UTC.
/// </para>
/// </summary>
public sealed class InlineEditValuesTests
{
    [Fact]
    public void A_date_input_sends_the_iso_form_and_gets_that_day_back()
    {
        Assert.True(InlineEditValues.TryParseDate("2026-08-21", out var value));
        Assert.Equal(new DateTime(2026, 8, 21), value);
    }

    [Fact]
    public void A_parsed_date_is_stamped_utc()
    {
        // Load-bearing. The column is `timestamp with time zone`, and Npgsql
        // rejects Unspecified outright — which is what TryParseExact produces
        // unless you say otherwise. Without this the edit throws at the database
        // rather than failing anywhere a person could see it.
        Assert.True(InlineEditValues.TryParseDate("2026-08-21", out var value));
        Assert.Equal(DateTimeKind.Utc, value.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tomorrow")]
    [InlineData("21/08/2026")]
    [InlineData("2026-13-01")]
    [InlineData("2026-02-30")]
    public void Anything_that_is_not_an_iso_day_is_refused(string? raw)
    {
        Assert.False(InlineEditValues.TryParseDate(raw, out _));
    }

    [Fact]
    public void An_amount_is_read_in_the_invariant_form_the_browser_sends()
    {
        Assert.True(InlineEditValues.TryParseAmount("89.20", out var value));
        Assert.Equal(89.20m, value);
    }

    [Fact]
    public void A_comma_decimal_is_refused()
    {
        // `input type="number"` may *display* a comma in a French locale, but
        // its `value` is always a valid floating-point number with a dot.
        // Accepting a comma would mean guessing at input the browser never
        // sends — and guessing wrong turns 89,20 into 8920.
        Assert.False(InlineEditValues.TryParseAmount("89,20", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("0.00")]
    public void An_amount_the_api_would_reject_never_leaves_the_page(string? raw)
    {
        // The DTO carries [Range(0.01, double.MaxValue)]. Catching it here turns
        // a 400 and a red toast into a field that simply does not commit.
        Assert.False(InlineEditValues.TryParseAmount(raw, out _));
    }

    [Fact]
    public void An_amount_is_rounded_to_cents()
    {
        Assert.True(InlineEditValues.TryParseAmount("89.207", out var value));
        Assert.Equal(89.21m, value);
    }

    [Fact]
    public void A_fraction_of_a_cent_does_not_sneak_a_zero_through()
    {
        // Refused as typed, not rounded up to a cent. Someone who types 0.004
        // has not asked for a one-cent bill, and guessing that they did is how
        // a zero-ish amount ends up on the books.
        Assert.False(InlineEditValues.TryParseAmount("0.004", out _));
    }

    [Fact]
    public void A_payee_is_trimmed()
    {
        Assert.True(InlineEditValues.TryParsePayee("  Verizon  ", out var value));
        Assert.Equal("Verizon", value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_payee_is_refused(string? raw)
    {
        Assert.False(InlineEditValues.TryParsePayee(raw, out _));
    }
}
