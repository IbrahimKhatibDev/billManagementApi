using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// <see cref="ReportRanges"/> — the preset date windows the Reports page, its CSV
/// export and the API's own <c>from</c>/<c>to</c> filtering all read from.
/// <para>
/// Three callers agreeing on one definition is the reason this arithmetic was put
/// in the contracts project, and until now nothing checked the definition itself:
/// the integration tests reach it only through a <c>from</c>/<c>to</c> pair they
/// spell out by hand, which is the answer rather than the sum. Everything here
/// takes <c>today</c> as an argument, so none of it depends on when it is run.
/// </para>
/// </summary>
public sealed class ReportRangeTests
{
    public static TheoryData<ReportRange> AllRanges()
    {
        var data = new TheoryData<ReportRange>();

        foreach (var range in ReportRanges.All)
        {
            data.Add(range);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllRanges))]
    public void Every_preset_can_be_read_back_from_its_own_slug(ReportRange range)
    {
        // The CSV endpoint puts the slug in a query string and gets it back on the
        // next request. A preset whose slug does not parse to itself exports the
        // wrong window, and does it quietly — the file downloads either way.
        Assert.Equal(range, ReportRanges.Parse(range.Slug()));
    }

    [Theory]
    [MemberData(nameof(AllRanges))]
    public void A_preset_can_also_be_named_the_way_the_code_names_it(ReportRange range)
    {
        Assert.Equal(range, ReportRanges.Parse(range.ToString()));
        Assert.Equal(range, ReportRanges.Parse(range.ToString().ToUpperInvariant()));
        Assert.Equal(range, ReportRanges.Parse(range.Slug().ToUpperInvariant()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("last-7-months")]
    [InlineData("this year")]
    [InlineData("42")]
    public void Anything_it_does_not_recognise_is_all_time(string? value)
    {
        // This parses a query string, so the input is whatever somebody typed.
        // Falling back beats throwing: the page still renders, showing more than
        // was asked for rather than an error, and "all time" is the one window
        // that cannot be a misreading of a narrower one.
        Assert.Equal(ReportRange.AllTime, ReportRanges.Parse(value));
    }

    [Fact]
    public void No_two_presets_answer_to_the_same_name()
    {
        // A shared slug would make one preset unreachable through Parse; a shared
        // label would put two buttons reading the same thing on the page.
        Assert.Equal(ReportRanges.All.Count, ReportRanges.All.Select(r => r.Slug()).Distinct().Count());
        Assert.Equal(ReportRanges.All.Count, ReportRanges.All.Select(r => r.Label()).Distinct().Count());
    }

    [Fact]
    public void This_year_is_the_calendar_year_and_not_the_last_twelve_months()
    {
        var (from, to) = ReportRange.ThisYear.Window(new DateTime(2026, 6, 15));

        Assert.Equal(new DateTime(2026, 1, 1), from);
        Assert.Equal(new DateTime(2026, 12, 31), to);

        // Which means it runs past today: a bill due in November is in "this
        // year" in June. That is the point — the page is as much about what is
        // still coming as about what has been.
        Assert.True(ReportRange.ThisYear.Includes(new DateTime(2026, 6, 15), new DateTime(2026, 11, 30)));
        Assert.False(ReportRange.ThisYear.Includes(new DateTime(2026, 6, 15), new DateTime(2025, 12, 31)));
    }

    [Fact]
    public void All_time_is_the_only_preset_without_ends()
    {
        var (unbounded, alsoUnbounded) = ReportRange.AllTime.Window(new DateTime(2026, 6, 15));

        Assert.Null(unbounded);
        Assert.Null(alsoUnbounded);

        foreach (var range in ReportRanges.All.Where(r => r != ReportRange.AllTime))
        {
            var (from, to) = range.Window(new DateTime(2026, 6, 15));

            Assert.NotNull(from);
            Assert.NotNull(to);
            Assert.True(from <= to, $"{range} runs backwards.");
        }
    }

    [Fact]
    public void A_bill_with_no_due_date_belongs_to_all_time_and_to_nothing_else()
    {
        var today = new DateTime(2026, 6, 15);

        Assert.True(ReportRange.AllTime.Includes(today, null));

        foreach (var range in ReportRanges.All.Where(r => r != ReportRange.AllTime))
        {
            // It cannot be placed on a timeline, so a bounded window has no
            // honest answer other than no. The page says how many were left out
            // whenever a preset is active, which is what stops this reading as a
            // disappearance.
            Assert.False(range.Includes(today, null), $"{range} kept an undated bill.");
        }
    }

    [Fact]
    public void Both_ends_of_a_window_are_days_that_count()
    {
        var today = new DateTime(2026, 6, 15);
        var (from, to) = ReportRange.Next3Months.Window(today);

        Assert.True(ReportRange.Next3Months.Includes(today, from));
        Assert.True(ReportRange.Next3Months.Includes(today, to));
        Assert.False(ReportRange.Next3Months.Includes(today, from!.Value.AddDays(-1)));
        Assert.False(ReportRange.Next3Months.Includes(today, to!.Value.AddDays(1)));
    }

    [Fact]
    public void A_time_of_day_does_not_push_a_bill_out_of_its_window()
    {
        // Four of the five presets take an end straight from `today`, which
        // carries whatever time it was computed at. So the boundary day is a day
        // whose bills are half before the bound and half after it, and a
        // comparison on the raw value would keep or drop them by the hour the
        // request happened to arrive. Comparing on .Date is what makes the answer
        // the same all day.
        var afternoon = new DateTime(2026, 6, 15, 17, 30, 0);

        // Due this morning, on a window that opens this afternoon.
        Assert.True(ReportRange.Next3Months.Includes(afternoon, new DateTime(2026, 6, 15, 9, 0, 0)));

        // Due tonight, on a window that closes this afternoon.
        Assert.True(ReportRange.Last3Months.Includes(afternoon, new DateTime(2026, 6, 15, 23, 0, 0)));

        // And midnight, which is how the API actually stores a due date.
        Assert.True(ReportRange.Next3Months.Includes(afternoon, new DateTime(2026, 6, 15)));
        Assert.True(ReportRange.Last3Months.Includes(afternoon, new DateTime(2026, 6, 15)));
    }

    [Fact]
    public void Three_months_on_from_the_end_of_a_long_month_is_the_end_of_a_short_one()
    {
        // AddMonths clamps rather than overflowing, so "next 3 months" from
        // March 31 ends on June 30 and not July 1. Pinned because the alternative
        // reading — that a window can gain a day depending on which month you
        // stand in — is the sort of thing a hand-rolled version does.
        var (_, to) = ReportRange.Next3Months.Window(new DateTime(2026, 3, 31));

        Assert.Equal(new DateTime(2026, 6, 30), to);
    }

    [Fact]
    public void Every_preset_says_on_the_page_what_it_covers()
    {
        var today = new DateTime(2026, 6, 15);

        Assert.Equal("Every bill, whenever it is due.", ReportRange.AllTime.Caption(today));

        foreach (var range in ReportRanges.All.Where(r => r != ReportRange.AllTime))
        {
            var (from, to) = range.Window(today);
            var caption = range.Caption(today);

            // Both ends named, so no preset has to be guessed at. Asserted by
            // formatting the ends the same way rather than against a literal:
            // Caption uses the current culture, so a machine set to anything but
            // English spells the months differently and a pinned string would
            // fail there for no reason worth failing over.
            Assert.Contains(from!.Value.ToString("MMM d, yyyy"), caption);
            Assert.Contains(to!.Value.ToString("MMM d, yyyy"), caption);
        }
    }
}
