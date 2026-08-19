using BillsMinimalApi.Data;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// <see cref="UtcDateTime.Normalize(DateTime)"/> — the rule every date in this
/// app passes through before Npgsql sees it.
/// <para>
/// The integration suite proves the round trip: a date sent in comes back as the
/// same date. What it cannot show is *why*, because it runs against a container
/// whose clock is UTC, where the wrong rule and the right one agree. These tests
/// are about the branch, not the round trip.
/// </para>
/// </summary>
public sealed class UtcDateTimeTests
{
    [Fact]
    public void A_date_that_names_no_timezone_is_relabelled_and_not_moved()
    {
        // The whole point. "2026-03-15" off the wire, or an <input type="date">
        // binding, arrives as Unspecified — it carries no offset, so there is
        // nothing to convert *from*. ToUniversalTime() would invent one from
        // whatever machine happened to be running, and store a different instant
        // on a developer's Mac than in the container.
        var value = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Unspecified);

        var normalized = UtcDateTime.Normalize(value);

        Assert.Equal(value.Ticks, normalized.Ticks);
        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
    }

    [Fact]
    public void A_date_already_in_utc_is_handed_straight_back()
    {
        var value = new DateTime(2026, 3, 15, 13, 45, 0, DateTimeKind.Utc);

        Assert.Equal(value, UtcDateTime.Normalize(value));
        Assert.Equal(DateTimeKind.Utc, UtcDateTime.Normalize(value).Kind);
    }

    [Fact]
    public void A_local_time_is_converted_because_it_does_name_a_timezone()
    {
        // The other half of the rule, and the reason Unspecified needs a branch
        // of its own: Local knows what it is an offset from, so shifting it is
        // right here and wrong one test up. Bogus produces these, as does any
        // JSON timestamp carrying an offset that is not Z.
        var value = new DateTime(2026, 3, 15, 13, 45, 0, DateTimeKind.Local);
        var offset = TimeZoneInfo.Local.GetUtcOffset(value);

        var normalized = UtcDateTime.Normalize(value);

        Assert.Equal(value.Ticks - offset.Ticks, normalized.Ticks);
        Assert.Equal(DateTimeKind.Utc, normalized.Kind);

        // Worth being straight about: on a machine set to UTC — CI, and the
        // container this ships in — that offset is zero and the assertion above
        // cannot tell a conversion from a relabel. It is the test one up that
        // carries the rule everywhere, and this one that catches a regression
        // when somebody runs the suite in a timezone.
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Normalizing_twice_says_the_same_thing_as_normalizing_once(DateTimeKind kind)
    {
        // Dates pass through here on the way into the DbContext and again on the
        // way out of a query's parameters, so this is not a theoretical property:
        // a rule that shifted on every call would drift by an hour per hop.
        var value = new DateTime(2026, 3, 15, 13, 45, 0, kind);

        var once = UtcDateTime.Normalize(value);

        Assert.Equal(once, UtcDateTime.Normalize(once));
    }

    [Fact]
    public void No_date_stays_null_and_anything_else_gets_the_same_treatment()
    {
        // The nullable overload exists for optional due dates and for the
        // from/to window, where "not given" has to survive as not given rather
        // than become the start of the epoch.
        Assert.Null(UtcDateTime.Normalize((DateTime?)null));

        var value = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(UtcDateTime.Normalize(value), UtcDateTime.Normalize((DateTime?)value));
    }

    [Fact]
    public void Today_is_a_whole_day_in_utc()
    {
        // Sampled either side rather than compared to one reading, so a run that
        // straddles midnight UTC does not fail for having been unlucky.
        var before = DateTime.UtcNow.Date;
        var today = UtcDateTime.Today;
        var after = DateTime.UtcNow.Date;

        // Midnight, because that is what a due date is stored as: comparing
        // against anything carrying a time would make "overdue" flip partway
        // through the day instead of at the moment the stored value does.
        Assert.Equal(TimeSpan.Zero, today.TimeOfDay);
        Assert.Equal(DateTimeKind.Utc, today.Kind);
        Assert.True(today == before || today == after, "Today is not today.");
    }
}
