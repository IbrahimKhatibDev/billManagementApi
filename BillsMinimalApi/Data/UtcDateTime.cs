namespace BillsMinimalApi.Data;

/// <summary>
/// The single definition of "what UTC means" in this app.
/// <para>
/// Npgsql maps <see cref="DateTime"/> to <c>timestamp with time zone</c> and
/// throws on any value whose <see cref="DateTime.Kind"/> is not
/// <see cref="DateTimeKind.Utc"/>. Values arrive here as
/// <see cref="DateTimeKind.Unspecified"/> (JSON dates such as "2026-03-15", and
/// the Blazor <c>&lt;input type="date"&gt;</c> binding) or
/// <see cref="DateTimeKind.Local"/> (Bogus, and JSON values carrying a non-Zulu
/// offset).
/// </para>
/// <para>
/// <see cref="DateTimeKind.Unspecified"/> is treated as *already* UTC rather
/// than converted from local time: a date-only payload carries no timezone, so
/// <c>ToUniversalTime()</c> would shift it by the host offset and store a
/// different instant on a developer's Mac than in the (UTC) container.
/// </para>
/// </summary>
public static class UtcDateTime
{
    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime(),
    };

    public static DateTime? Normalize(DateTime? value) =>
        value.HasValue ? Normalize(value.Value) : null;
}
