namespace BillsMinimalApi.Tests;

/// <summary>
/// <c>BillSummary.Weeks</c> end to end: the SQL grouping and the fold, over a
/// real Postgres, against the same window every other aggregate describes.
/// </summary>
public sealed class BillTimelineTests(PostgresApiFixture fixture) : ApiTestBase(fixture)
{
    private static DateTime Day(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Bills_falling_in_one_week_share_a_column()
    {
        await Fixture.CreateBillAsync(paymentDue: 100m, paid: true, dueDate: Day(2026, 3, 16));
        await Fixture.CreateBillAsync(paymentDue: 250m, paid: false, dueDate: Day(2026, 3, 20));

        var summary = await Fixture.GetSummaryAsync();

        var week = Assert.Single(summary.Weeks);
        Assert.Equal(Day(2026, 3, 16), week.WeekStart);
        Assert.Equal(2, week.Bills);
        Assert.Equal(100m, week.Paid);
        Assert.Equal(250m, week.Unpaid);
    }

    [Fact]
    public async Task The_timeline_runs_through_the_weeks_with_nothing_in_them()
    {
        await Fixture.CreateBillAsync(paymentDue: 100m, dueDate: Day(2026, 3, 16));
        await Fixture.CreateBillAsync(paymentDue: 200m, dueDate: Day(2026, 3, 30));

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(3, summary.Weeks.Count);
        Assert.Equal(Day(2026, 3, 23), summary.Weeks[1].WeekStart);
        Assert.Equal(0, summary.Weeks[1].Bills);
    }

    [Fact]
    public async Task The_timeline_describes_the_requested_window_like_everything_else()
    {
        // A range that excludes a bill excludes its column too — the timeline is
        // not a second, wider view of the same page.
        await Fixture.CreateBillAsync(paymentDue: 100m, dueDate: Day(2026, 3, 16));
        await Fixture.CreateBillAsync(paymentDue: 200m, dueDate: Day(2026, 6, 15));

        var summary = await Fixture.GetSummaryAsync("from=2026-03-01&to=2026-03-31");

        var week = Assert.Single(summary.Weeks);
        Assert.Equal(Day(2026, 3, 16), week.WeekStart);
    }

    [Fact]
    public async Task An_empty_book_draws_no_timeline_rather_than_failing()
    {
        var summary = await Fixture.GetSummaryAsync();

        Assert.Empty(summary.Weeks);
    }
}
