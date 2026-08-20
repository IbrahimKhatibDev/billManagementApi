# Bills Manager Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the Blazor frontend to the redesign in `docs/superpowers/specs/2026-08-19-bills-manager-redesign-design.md` — four palette×mode themes, Phosphor icons, and all ten UX changes across Overview, Bills and Reports — plus the four backend aggregates and the free-text parse endpoint they need.

**Architecture:** A CSS custom-property token layer sits under the existing Bootstrap 5 build; every colour in the app becomes `var(--token)` and the four palettes are four blocks keyed off `<html data-palette data-mode>`. The ten ideas are extracted into small components under `bills-frontend/BillsFrontEndBlazor/Shared/`, each fed by aggregates the server already computes. Four new aggregates (`Weeks`, `Late`, `OldestDaysLate`, and the parse endpoint) extend `BillSummaryBuilder` and `BillEndPoints` rather than introducing a new query path. Pure client-side logic that deserves a unit test (due-window classification, Pareto arithmetic, week bucketing) lives in `BillsMinimalApi.Contracts`, the only assembly both the Blazor app and the unit-test project reference.

**Tech Stack:** .NET 10 minimal API, EF Core + Npgsql/Postgres, Blazor Server (`ServerPrerendered`), Bootstrap 5 + Bootstrap 5.3 `data-bs-theme`, inline SVG charts rendered from C#, xUnit (Testcontainers integration suite + plain unit suite).

## Global Constraints

Every task's requirements implicitly include this section.

- **Verification command:** `dotnet test BillsMinimalApi/BillsMinimalApi.sln`. The solution contains all five projects including `BillsFrontEndBlazor`, so this compiles the frontend too. Integration tests need Docker running. For a frontend-only task where no test changed, `dotnet build BillsMinimalApi/BillsMinimalApi.sln` is enough.
- **No new dependencies.** `BillsFrontEndBlazor.csproj` has zero `PackageReference` entries and keeps zero. No NuGet, no npm, no build step. Bootstrap 5 stays.
- **Every asset self-hosted** under `bills-frontend/BillsFrontEndBlazor/wwwroot/`. No CDN links, no runtime fetches: the UI must render with the network off, and the Docker build must stay hermetic.
- **No JS interop during the first render.** The app renders `ServerPrerendered`; `IJSRuntime` is unusable until `OnAfterRenderAsync`. Charts are inline SVG rendered from C# with no interop and no fetch.
- **Colours only via `var(--token)`.** No hex literal appears anywhere outside `wwwroot/css/tokens.css`.
- **No drop shadows.** `1px solid var(--border)` is the only elevation cue.
- **Accent is outline-only in Nocturne** — never a flood fill.
- **Desktop-first, min-width 1240px.** Below 1240px the existing drawer/rail must keep working, re-skinned but undesigned. Do not invent a mobile treatment; Task 15 flags it as the follow-up the handoff asks for.
- **Copy is verbatim from the spec.** Sentences, labels and button text are final design content, not paraphrasable.
- **Money is `decimal`, never `double`.** Percentages may be `double`.
- **Anything reaching Npgsql carries `DateTimeKind.Utc`.** Use `UtcDateTime.Today` on the server; construct dates with the explicit `DateTimeKind.Utc` overload.
- **No bUnit.** Components are not unit-tested; logic worth testing is kept out of components and put in `BillsMinimalApi.Contracts`.

---

## File Structure

Frontend paths are relative to `bills-frontend/BillsFrontEndBlazor/`. The task
each file belongs to is in brackets.

**Backend — created**
- `BillsMinimalApi/Parsing/BillTextParser.cs` — the free-text grammar. [1]
- `BillsMinimalApi.Contracts/ParsedBill.cs` — `ParseBillRequest`, `ParsedBill`, `ParseConfidence`. [1]
- `BillsMinimalApi.Contracts/WeekBuckets.cs` — the pure day-rows→weeks fold. [3]

**Backend — modified**
- `BillsMinimalApi.Contracts/BillSummary.cs` — adds the `WeekTotals` row type plus `Weeks`, `MaxWeeks` [3], `Late`, `LateCount`, `OldestDaysLate` [4].
- `BillsMinimalApi/Queries/BillSummaryBuilder.cs` — adds `WeeksAsync` [3] and `LateAsync` [4].
- `BillsMinimalApi/Endpoints/BillEndPoints.cs` — adds `POST /restapi/BillDtos/parse`. [2]

**Contracts — the shared logic layer**

Every piece of client-side arithmetic worth a test lives here rather than in a
component, because `BillsMinimalApi.Contracts` is the only assembly both
`BillsFrontEndBlazor` and `BillsMinimalApi.UnitTests` reference — and there is
no bUnit. A component that computes nothing needs no component test.

- `BillsMinimalApi.Contracts/ObligationSentence.cs` — the Overview headline. [7]
- `BillsMinimalApi.Contracts/StackedStrip.cs` — segment widths for the aging bar. [7]
- `BillsMinimalApi.Contracts/TimelineLayout.cs` — week bars, axis and the today marker. [8]
- `BillsMinimalApi.Contracts/DueWindows.cs` — `DueWindow` and its classifier. [9]
- `BillsMinimalApi.Contracts/BulkPaidOutcome.cs` — what a partly-failed batch says. [10]
- `BillsMinimalApi.Contracts/InlineEditValues.cs` — parsing and validating an edited cell. [11]
- `BillsMinimalApi.Contracts/ParsedBillReading.cs` — the correctable reading of a parse. [12]
- `BillsMinimalApi.Contracts/NumberWords.cs` — `Spell(int)`, shared by [13] and [14].
- `BillsMinimalApi.Contracts/ParetoRows.cs` — `ParetoRow`, cumulative share, `PayeesToReach`, `Headline`. [13]
- `BillsMinimalApi.Contracts/SizeBandSentence.cs` — `Describe`, `Phrase`. [14]

**Frontend — created**
- `wwwroot/css/tokens.css` — the four palette×mode blocks and the shared shape tokens [5]; gains the Inter `@font-face` [6] and `--scrim` [15].
- `wwwroot/js/theme.js` — flash-free boot plus `window.billsTheme`. [5]
- `Shared/ThemeSwitcher.razor` + `.razor.css` — the two independent toggles. [5]
- `wwwroot/css/phosphor/`, `wwwroot/css/fonts/inter/` — vendored icon font and typeface. [6]
- `Shared/Icon.razor` — the one place a Phosphor class name is written. [6]
- `Shared/ObligationHeadline.razor` + `.razor.css`, `Shared/AgingStrip.razor` + `.razor.css` — Overview, ideas 1 and 4. [7]
- `Shared/CashFlowTimeline.razor` + `.razor.css`, `Shared/LateBillsList.razor` + `.razor.css` — Overview, ideas 2 and 3. [8]
- `Pages/Index.razor.css` [8], `Pages/Bills.razor.css` [9], `Pages/Reports.razor.css` [14] — the three screens' own gutters and grids.
- `Shared/BillGroup.razor` + `.razor.css` — Bills, idea 5. [9]
- `Shared/BulkActionBar.razor` + `.razor.css` — Bills, idea 6. [10]
- `Shared/InlineEdit.razor` + `.razor.css`, `Models/BillEdit.cs` — Bills, idea 7. [11]
- `Shared/QuickAddBill.razor` + `.razor.css` — Bills, idea 8. [12]
- `Shared/PayeePareto.razor` + `.razor.css` — Reports, idea 9. [13]
- `Shared/PaidRateStrip.razor` + `.razor.css` — Reports, idea 10. [14]

**Frontend — modified**
- `Pages/_Layout.cshtml`, `Pages/_AccountLayout.cshtml` — token stylesheet and boot script in both [5]; the icon stylesheet swap [6].
- `Pages/Index.razor` + `.razor.cs` — rewritten. [8]
- `Pages/Bills.razor` + `.razor.cs` — rewritten [9], then extended by [10], [11] and [12].
- `Pages/Reports.razor` + `.razor.cs` — rewritten. [14]
- `Shared/NavMenu.razor` — the switcher drops in [5], icons swap [6]; `NavMenu.razor.css` re-skinned [15].
- `Shared/MainLayout.razor` + `.razor.css` — icons swap [6]; re-skinned, and the double gutter goes [15].
- `Services/BillService.cs` — `ParseBillAsync` [2], `MarkPaidAsync` [8], `BillBook`/`GetBookAsync` [9], `MarkManyPaidAsync` [10].
- `wwwroot/css/site.css` — the font token [5]; then the three orphaned sections deleted and the rest re-expressed in tokens [15].

**Frontend — deleted**
- `wwwroot/css/bootstrap-icons/` — the `.min.css` and its two font files. [6]

**Tests — created**
- `tests/BillsMinimalApi.UnitTests/BillTextParserTests.cs` [1]
- `tests/BillsMinimalApi.UnitTests/WeekBucketTests.cs` [3]
- `tests/BillsMinimalApi.UnitTests/ObligationSentenceTests.cs`, `StackedStripTests.cs` [7]
- `tests/BillsMinimalApi.UnitTests/TimelineLayoutTests.cs` [8]
- `tests/BillsMinimalApi.UnitTests/DueWindowsTests.cs` [9]
- `tests/BillsMinimalApi.UnitTests/BulkPaidOutcomeTests.cs` [10]
- `tests/BillsMinimalApi.UnitTests/InlineEditValuesTests.cs` [11]
- `tests/BillsMinimalApi.UnitTests/ParsedBillReadingTests.cs` [12]
- `tests/BillsMinimalApi.UnitTests/ParetoRowTests.cs` [13]
- `tests/BillsMinimalApi.UnitTests/SizeBandSentenceTests.cs` [14]
- `tests/BillsMinimalApi.Tests/ParseBillTests.cs` [2]
- `tests/BillsMinimalApi.Tests/BillTimelineTests.cs` [3]
- `tests/BillsMinimalApi.Tests/BillLateListTests.cs` [4]

**Tests — modified**
- `tests/BillsMinimalApi.Tests/PostgresApiFixture.cs` — adds `Routes.Parse`. [2]

**Docs — created**
- `docs/mobile-layout-follow-up.md` — the follow-up the handoff asks for. [15]

---

### Task 1: Free-text bill grammar

The parser is pure and takes `today` as an argument, so it is a unit test with no host and no clock. The endpoint that calls it is Task 2.

**Files:**
- Create: `BillsMinimalApi.Contracts/ParsedBill.cs`
- Create: `BillsMinimalApi/Parsing/BillTextParser.cs`
- Test: `tests/BillsMinimalApi.UnitTests/BillTextParserTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `BillsMinimalApi.Contracts.ParseBillRequest { string Text }`
  - `BillsMinimalApi.Contracts.ParsedBill { string? Payee; decimal? Amount; DateTime? DueDate; string Confidence }`
  - `BillsMinimalApi.Contracts.ParseConfidence.High` = `"high"`, `.Low` = `"low"`
  - `BillsMinimalApi.Parsing.BillTextParser.Parse(string? text, DateTime today) -> ParsedBill`

- [ ] **Step 1: Write the contract types**

Create `BillsMinimalApi.Contracts/ParsedBill.cs`:

```csharp
namespace BillsMinimalApi.Contracts;

/// <summary>Body of <c>POST /restapi/BillDtos/parse</c>.</summary>
public sealed class ParseBillRequest
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// What the server made of a line like "Verizon 89.20 fri". Nothing is
/// committed: every field is nullable so the client can render the reading and
/// let the user correct it before posting a real bill.
/// </summary>
public sealed class ParsedBill
{
    public string? Payee { get; set; }

    public decimal? Amount { get; set; }

    public DateTime? DueDate { get; set; }

    /// <summary>
    /// <see cref="ParseConfidence.High"/> only when all three fields resolved.
    /// A string rather than an enum because it crosses the wire as one.
    /// </summary>
    public string Confidence { get; set; } = ParseConfidence.Low;
}

public static class ParseConfidence
{
    public const string High = "high";

    public const string Low = "low";
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/BillsMinimalApi.UnitTests/BillTextParserTests.cs`:

```csharp
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
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln --filter FullyQualifiedName~BillTextParserTests`
Expected: build error — `The type or namespace name 'Parsing' does not exist in the namespace 'BillsMinimalApi'`.

- [ ] **Step 4: Write the parser**

Create `BillsMinimalApi/Parsing/BillTextParser.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.Parsing;

/// <summary>
/// Reads "Verizon 89.20 fri" as payee, amount and due date.
/// <para>
/// The grammar is positional and deliberately small: the payee is everything up
/// to the first number, the amount is that number, and the date is whatever
/// follows it. Anything outside the grammar comes back as a partial reading with
/// <see cref="ParseConfidence.Low"/> rather than an error — the client shows the
/// reading and lets the user fix it, so a miss costs a keystroke, not a failure.
/// </para>
/// </summary>
public static partial class BillTextParser
{
    private static readonly string[] Prepositions = { "due", "on", "by", "at" };

    private static readonly Dictionary<string, DayOfWeek> Weekdays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sun"] = DayOfWeek.Sunday,
        ["sunday"] = DayOfWeek.Sunday,
        ["mon"] = DayOfWeek.Monday,
        ["monday"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday,
        ["tues"] = DayOfWeek.Tuesday,
        ["tuesday"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday,
        ["weds"] = DayOfWeek.Wednesday,
        ["wednesday"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday,
        ["thur"] = DayOfWeek.Thursday,
        ["thurs"] = DayOfWeek.Thursday,
        ["thursday"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday,
        ["friday"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday,
        ["saturday"] = DayOfWeek.Saturday,
    };

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"] = 1, ["january"] = 1,
        ["feb"] = 2, ["february"] = 2,
        ["mar"] = 3, ["march"] = 3,
        ["apr"] = 4, ["april"] = 4,
        ["may"] = 5,
        ["jun"] = 6, ["june"] = 6,
        ["jul"] = 7, ["july"] = 7,
        ["aug"] = 8, ["august"] = 8,
        ["sep"] = 9, ["sept"] = 9, ["september"] = 9,
        ["oct"] = 10, ["october"] = 10,
        ["nov"] = 11, ["november"] = 11,
        ["dec"] = 12, ["december"] = 12,
    };

    // The lookarounds are what keep "8/21" out of the amount: a digit touching a
    // slash or a dot on either side is part of something else.
    [GeneratedRegex(@"(?<![\d./])\$?(?<amount>\d+(?:\.\d{1,2})?)(?![\d./])")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"^(?<m>\d{1,2})/(?<d>\d{1,2})(?:/(?<y>\d{2}|\d{4}))?$")]
    private static partial Regex NumericDatePattern();

    [GeneratedRegex(@"^(?<name>[A-Za-z]+)\.?\s+(?<day>\d{1,2})$")]
    private static partial Regex MonthFirstPattern();

    [GeneratedRegex(@"^(?<day>\d{1,2})\s+(?<name>[A-Za-z]+)\.?$")]
    private static partial Regex DayFirstPattern();

    /// <param name="today">
    /// Midnight UTC of the day the request arrived. Passed in rather than read
    /// from a clock so "fri" is a testable answer.
    /// </param>
    public static ParsedBill Parse(string? text, DateTime today)
    {
        var parsed = new ParsedBill { Confidence = ParseConfidence.Low };

        if (string.IsNullOrWhiteSpace(text))
        {
            return parsed;
        }

        var input = text.Trim();
        var amount = AmountPattern().Match(input);

        if (!amount.Success)
        {
            // No number anywhere: the line is a payee and nothing else.
            parsed.Payee = Clean(input);
            return parsed;
        }

        parsed.Payee = Clean(input[..amount.Index]);
        parsed.Amount = decimal.Parse(
            amount.Groups["amount"].Value, CultureInfo.InvariantCulture);

        if (TryResolveDate(input[(amount.Index + amount.Length)..], today, out var due))
        {
            parsed.DueDate = due;
        }

        if (!string.IsNullOrEmpty(parsed.Payee) && parsed.DueDate is not null)
        {
            parsed.Confidence = ParseConfidence.High;
        }

        return parsed;
    }

    private static string? Clean(string value)
    {
        var trimmed = value.Trim().Trim('-', ',', ':', ';').Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool TryResolveDate(string phrase, DateTime today, out DateTime due)
    {
        due = default;

        var words = phrase
            .Trim()
            .Trim(',', '.', ';', ':', '-')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var start = 0;
        while (start < words.Length
               && Prepositions.Contains(words[start], StringComparer.OrdinalIgnoreCase))
        {
            start++;
        }

        var cleaned = string.Join(' ', words.Skip(start));
        if (cleaned.Length == 0)
        {
            return false;
        }

        if (string.Equals(cleaned, "today", StringComparison.OrdinalIgnoreCase))
        {
            due = today.Date;
            return true;
        }

        if (string.Equals(cleaned, "tomorrow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cleaned, "tmrw", StringComparison.OrdinalIgnoreCase))
        {
            due = today.Date.AddDays(1);
            return true;
        }

        if (Weekdays.TryGetValue(cleaned, out var weekday))
        {
            // Strictly forward, 1 to 7 days out.
            var delta = ((int)weekday - (int)today.DayOfWeek + 7) % 7;
            due = today.Date.AddDays(delta == 0 ? 7 : delta);
            return true;
        }

        var numeric = NumericDatePattern().Match(cleaned);
        if (numeric.Success)
        {
            int? year = numeric.Groups["y"].Success
                ? NormalizeYear(int.Parse(numeric.Groups["y"].Value, CultureInfo.InvariantCulture))
                : null;

            return TryBuild(
                int.Parse(numeric.Groups["m"].Value, CultureInfo.InvariantCulture),
                int.Parse(numeric.Groups["d"].Value, CultureInfo.InvariantCulture),
                year,
                today,
                out due);
        }

        var monthFirst = MonthFirstPattern().Match(cleaned);
        if (monthFirst.Success && Months.TryGetValue(monthFirst.Groups["name"].Value, out var month))
        {
            return TryBuild(
                month,
                int.Parse(monthFirst.Groups["day"].Value, CultureInfo.InvariantCulture),
                null,
                today,
                out due);
        }

        var dayFirst = DayFirstPattern().Match(cleaned);
        if (dayFirst.Success && Months.TryGetValue(dayFirst.Groups["name"].Value, out month))
        {
            return TryBuild(
                month,
                int.Parse(dayFirst.Groups["day"].Value, CultureInfo.InvariantCulture),
                null,
                today,
                out due);
        }

        return false;
    }

    private static int NormalizeYear(int year) => year < 100 ? 2000 + year : year;

    private static bool TryBuild(int month, int day, int? year, DateTime today, out DateTime due)
    {
        due = default;

        if (month is < 1 or > 12)
        {
            return false;
        }

        var resolved = year ?? today.Year;
        if (day < 1 || day > DateTime.DaysInMonth(resolved, month))
        {
            return false;
        }

        var candidate = new DateTime(resolved, month, day, 0, 0, 0, DateTimeKind.Utc);

        // "mar 3" typed in August means next March. Only a year the user did not
        // supply may be rolled forward.
        if (year is null && candidate < today.Date)
        {
            if (day > DateTime.DaysInMonth(resolved + 1, month))
            {
                return false;
            }

            candidate = new DateTime(resolved + 1, month, day, 0, 0, 0, DateTimeKind.Utc);
        }

        due = candidate;
        return true;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln --filter FullyQualifiedName~BillTextParserTests`
Expected: PASS, 12 tests (the `[Theory]` contributes 3).

- [ ] **Step 6: Commit**

```bash
git add BillsMinimalApi.Contracts/ParsedBill.cs BillsMinimalApi/Parsing/BillTextParser.cs tests/BillsMinimalApi.UnitTests/BillTextParserTests.cs
git commit -m "Read a bill out of a line of text"
```

---

### Task 2: The parse endpoint and its client

The handoff writes the route as `POST /bills/parse` after an "e.g."; it goes in the existing group instead, so every bill route shares one prefix and one auth story.

**Files:**
- Modify: `BillsMinimalApi/Endpoints/BillEndPoints.cs:92-94` (insert between `/summary` and `/{id:long}`)
- Modify: `tests/BillsMinimalApi.Tests/PostgresApiFixture.cs:234-239` (add `Routes.Parse`)
- Modify: `bills-frontend/BillsFrontEndBlazor/Services/BillService.cs:221` (add `ParseBillAsync` after `DeleteBillAsync`)
- Test: `tests/BillsMinimalApi.Tests/ParseBillTests.cs`

**Interfaces:**
- Consumes: `BillTextParser.Parse(string?, DateTime)`, `ParseBillRequest`, `ParsedBill`, `ParseConfidence` (Task 1).
- Produces:
  - `POST /restapi/BillDtos/parse`, body `ParseBillRequest`, 200 with `ParsedBill`.
  - `Routes.Parse` = `"/restapi/BillDtos/parse"` for the integration suite.
  - `BillService.ParseBillAsync(string text, CancellationToken ct = default) -> Task<ParsedBill?>` — null means the server could not be asked.

- [ ] **Step 1: Write the failing tests**

Create `tests/BillsMinimalApi.Tests/ParseBillTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.Tests;

/// <summary>
/// The parse endpoint reads a line and writes nothing. The grammar itself is
/// covered exhaustively in the unit suite; what matters here is that the route
/// exists, is closed like every other, and leaves the table alone.
/// </summary>
public sealed class ParseBillTests(PostgresApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task A_line_of_text_comes_back_read()
    {
        // "today" rather than "fri", because the server resolves against its own
        // clock and only "today" is a date this test can name.
        var response = await Client.PostAsJsonAsync(
            Routes.Parse, new ParseBillRequest { Text = "Verizon 89.20 today" });

        response.EnsureSuccessStatusCode();
        var parsed = (await response.Content.ReadFromJsonAsync<ParsedBill>())!;

        Assert.Equal("Verizon", parsed.Payee);
        Assert.Equal(89.20m, parsed.Amount);
        Assert.Equal(DateTime.UtcNow.Date, parsed.DueDate);
        Assert.Equal(ParseConfidence.High, parsed.Confidence);
    }

    [Fact]
    public async Task Reading_a_bill_does_not_create_one()
    {
        // The whole point of the endpoint: the user gets to correct the reading
        // before anything is committed.
        await Client.PostAsJsonAsync(
            Routes.Parse, new ParseBillRequest { Text = "Verizon 89.20 today" });

        var page = await Fixture.GetPageAsync();

        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task A_line_the_grammar_cannot_place_is_a_low_reading_and_not_an_error()
    {
        var response = await Client.PostAsJsonAsync(
            Routes.Parse, new ParseBillRequest { Text = "pay the gas people" });

        response.EnsureSuccessStatusCode();
        var parsed = (await response.Content.ReadFromJsonAsync<ParsedBill>())!;

        Assert.Equal(ParseConfidence.Low, parsed.Confidence);
        Assert.Null(parsed.Amount);
    }

    [Fact]
    public async Task Parsing_is_closed_to_anonymous_callers_like_everything_else()
    {
        var response = await Fixture.AnonymousClient.PostAsJsonAsync(
            Routes.Parse, new ParseBillRequest { Text = "Verizon 89.20 today" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Add the route constant**

In `tests/BillsMinimalApi.Tests/PostgresApiFixture.cs`, extend the `Routes` class:

```csharp
public static class Routes
{
    public const string Bills = "/restapi/BillDtos";

    public const string Summary = Bills + "/summary";

    public const string Parse = Bills + "/parse";
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln --filter FullyQualifiedName~ParseBillTests`
Expected: FAIL — the POST returns 404, so `EnsureSuccessStatusCode` throws `Response status code does not indicate success: 404 (Not Found)`.

- [ ] **Step 4: Map the endpoint**

In `BillsMinimalApi/Endpoints/BillEndPoints.cs`, add the using:

```csharp
using BillsMinimalApi.Parsing;
```

and insert this between the `/summary` handler and the `// GET BY ID` comment:

```csharp
            // PARSE A LINE OF TEXT
            //
            // Reads and returns; it never touches the DbContext. The client
            // renders the reading, the user corrects it, and the bill is created
            // through POST "/" like any other — so a misread costs a keystroke
            // rather than a row that has to be found and fixed.
            //
            // Today is read here rather than in the parser so that "fri" is
            // resolved against the server's clock, which is the same clock every
            // other date comparison in this API uses.
            group.MapPost("/parse", (ParseBillRequest request) =>
                Results.Ok(BillTextParser.Parse(request.Text, UtcDateTime.Today)));
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln --filter FullyQualifiedName~ParseBillTests`
Expected: PASS, 4 tests.

- [ ] **Step 6: Add the client method**

In `bills-frontend/BillsFrontEndBlazor/Services/BillService.cs`, add after `DeleteBillAsync`:

```csharp
        /// <summary>
        /// The server's reading of a line like "Verizon 89.20 fri". Nothing is
        /// created — the caller shows the reading for confirmation and then posts
        /// a real bill through <see cref="CreateBillAsync"/>.
        /// </summary>
        /// <returns>
        /// Null when there is no reading to show: the server was unreachable, it
        /// refused, or a newer keystroke cancelled this call. All three mean the
        /// preview stays as it was.
        /// </returns>
        public async Task<ParsedBill?> ParseBillAsync(string text, CancellationToken ct = default)
        {
            await AuthorizeAsync();

            try
            {
                using var response = await _http.PostAsJsonAsync(
                    $"{Route}/parse", new ParseBillRequest { Text = text }, ct);

                return response.IsSuccessStatusCode
                    ? await response.Content.ReadFromJsonAsync<ParsedBill>(ct)
                    : null;
            }
            catch (OperationCanceledException)
            {
                // The caller debounces typing, so a cancelled read is the normal
                // case, not a fault. Rethrowing would surface every superseded
                // keystroke as an unhandled exception and tear down the circuit.
                return null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }
```

- [ ] **Step 7: Verify the whole solution still builds and passes**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln`
Expected: PASS, no build warnings introduced.

- [ ] **Step 8: Commit**

```bash
git add BillsMinimalApi/Endpoints/BillEndPoints.cs bills-frontend/BillsFrontEndBlazor/Services/BillService.cs tests/BillsMinimalApi.Tests/ParseBillTests.cs tests/BillsMinimalApi.Tests/PostgresApiFixture.cs
git commit -m "Offer a reading of a bill without writing one"
```

---

### Task 3: Weekly totals for the cash-flow timeline

`Months` is monthly and the timeline is weekly, so this is a new aggregate rather than a reshape of an old one. Postgres groups by due date and C# folds days into Monday-start weeks: `date_trunc('week', …)` has no dependable Npgsql translation, and the fold is the part worth testing anyway.

**Files:**
- Modify: `BillsMinimalApi.Contracts/BillSummary.cs:23` (add `MaxWeeks`), `:80` (add `Weeks`), `:156` (add `WeekTotals`)
- Create: `BillsMinimalApi.Contracts/WeekBuckets.cs`
- Modify: `BillsMinimalApi/Queries/BillSummaryBuilder.cs` (add `WeeksAsync`, call it from `BuildAsync`)
- Test: `tests/BillsMinimalApi.UnitTests/WeekBucketTests.cs`
- Test: `tests/BillsMinimalApi.Tests/BillTimelineTests.cs`

**Interfaces:**
- Consumes: `BillSummary` (existing).
- Produces:
  - `BillsMinimalApi.Contracts.WeekTotals { DateTime WeekStart; int Bills; decimal Paid; decimal Unpaid; decimal Total => Paid + Unpaid }`
  - `BillSummary.Weeks : List<WeekTotals>`, `BillSummary.MaxWeeks` = `260`
  - `WeekBuckets.DayTotals(DateTime Day, int Bills, decimal Paid, decimal Unpaid)` — a `readonly record struct`
  - `WeekBuckets.StartOfWeek(DateTime day) -> DateTime`
  - `WeekBuckets.FromDays(IEnumerable<DayTotals> days, int maxWeeks) -> List<WeekTotals>`

- [ ] **Step 1: Write the failing unit tests for the fold**

Create `tests/BillsMinimalApi.UnitTests/WeekBucketTests.cs`:

```csharp
using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// The fold behind the cash-flow timeline. Postgres hands back one row per
/// distinct due date; this turns those into the columns the chart draws.
/// </summary>
public sealed class WeekBucketTests
{
    private const int MaxWeeks = BillSummary.MaxWeeks;

    private static DateTime Day(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_week_starts_on_Monday()
    {
        // 2026-08-19 is a Wednesday; its week began on the 17th.
        Assert.Equal(Day(2026, 8, 17), WeekBuckets.StartOfWeek(Day(2026, 8, 19)));
    }

    [Fact]
    public void Sunday_closes_the_week_rather_than_opening_the_next_one()
    {
        // The .NET DayOfWeek enum starts on Sunday, so this is the case the
        // shift in StartOfWeek exists for.
        Assert.Equal(Day(2026, 8, 17), WeekBuckets.StartOfWeek(Day(2026, 8, 23)));
    }

    [Fact]
    public void Days_inside_one_week_add_up_to_one_column()
    {
        var weeks = WeekBuckets.FromDays(new[]
        {
            new WeekBuckets.DayTotals(Day(2026, 8, 17), 1, 100m, 0m),
            new WeekBuckets.DayTotals(Day(2026, 8, 21), 2, 0m, 250m),
        }, MaxWeeks);

        var week = Assert.Single(weeks);
        Assert.Equal(Day(2026, 8, 17), week.WeekStart);
        Assert.Equal(3, week.Bills);
        Assert.Equal(100m, week.Paid);
        Assert.Equal(250m, week.Unpaid);
        Assert.Equal(350m, week.Total);
    }

    [Fact]
    public void Paid_and_unpaid_stay_apart_because_the_bar_is_stacked()
    {
        var weeks = WeekBuckets.FromDays(new[]
        {
            new WeekBuckets.DayTotals(Day(2026, 8, 18), 2, 40m, 60m),
        }, MaxWeeks);

        Assert.Equal(40m, weeks[0].Paid);
        Assert.Equal(60m, weeks[0].Unpaid);
    }

    [Fact]
    public void A_quiet_week_between_two_busy_ones_still_occupies_space()
    {
        // An empty column is the information — it says nothing falls due then.
        // Dropping it would slide the following week left and make the gap
        // invisible.
        var weeks = WeekBuckets.FromDays(new[]
        {
            new WeekBuckets.DayTotals(Day(2026, 8, 17), 1, 0m, 100m),
            new WeekBuckets.DayTotals(Day(2026, 8, 31), 1, 0m, 200m),
        }, MaxWeeks);

        Assert.Equal(3, weeks.Count);
        Assert.Equal(Day(2026, 8, 24), weeks[1].WeekStart);
        Assert.Equal(0, weeks[1].Bills);
        Assert.Equal(0m, weeks[1].Total);
    }

    [Fact]
    public void Weeks_come_back_oldest_first_whatever_order_the_days_arrived_in()
    {
        // GroupBy in Postgres promises no ordering, so the fold has to impose it.
        var weeks = WeekBuckets.FromDays(new[]
        {
            new WeekBuckets.DayTotals(Day(2026, 8, 31), 1, 0m, 200m),
            new WeekBuckets.DayTotals(Day(2026, 8, 17), 1, 0m, 100m),
        }, MaxWeeks);

        Assert.Equal(Day(2026, 8, 17), weeks[0].WeekStart);
        Assert.Equal(Day(2026, 8, 31), weeks[2].WeekStart);
    }

    [Fact]
    public void A_span_no_one_would_draw_keeps_the_bills_and_drops_the_gaps()
    {
        // One bill typed with the wrong year would otherwise turn the timeline
        // into ten thousand empty columns. The run stops being continuous rather
        // than stopping being complete.
        var weeks = WeekBuckets.FromDays(new[]
        {
            new WeekBuckets.DayTotals(Day(2026, 8, 17), 1, 0m, 100m),
            new WeekBuckets.DayTotals(Day(2126, 8, 17), 1, 0m, 200m),
        }, MaxWeeks);

        Assert.Equal(2, weeks.Count);
        Assert.Equal(Day(2026, 8, 17), weeks[0].WeekStart);
        Assert.Equal(Day(2126, 8, 17), weeks[1].WeekStart);
    }

    [Fact]
    public void No_bills_is_no_weeks_rather_than_one_empty_one()
    {
        Assert.Empty(WeekBuckets.FromDays(Array.Empty<WeekBuckets.DayTotals>(), MaxWeeks));
    }

    [Fact]
    public void The_week_start_is_UTC_because_it_is_compared_against_database_dates()
    {
        var weeks = WeekBuckets.FromDays(new[]
        {
            new WeekBuckets.DayTotals(Day(2026, 8, 19), 1, 0m, 100m),
        }, MaxWeeks);

        Assert.Equal(DateTimeKind.Utc, weeks[0].WeekStart.Kind);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln --filter FullyQualifiedName~WeekBucketTests`
Expected: build error — `The name 'WeekBuckets' does not exist in the current context`.

- [ ] **Step 3: Add the contract**

In `BillsMinimalApi.Contracts/BillSummary.cs`, add after the `PriorityCount` constant:

```csharp
    /// <summary>
    /// How long a run of weeks the cash-flow timeline will draw before it gives
    /// up on being continuous. Five years of columns is already more than a
    /// screen holds; past that the span is a typo, not a plan.
    /// </summary>
    public const int MaxWeeks = 260;
```

add after the `Months` property:

```csharp
    /// <summary>
    /// Every week the book touches, oldest first, with paid and unpaid kept
    /// apart so the column can stack. Weeks with nothing in them are included:
    /// same argument as <see cref="Aging"/> — a gap in the cash flow is what the
    /// chart is for.
    /// </summary>
    public List<WeekTotals> Weeks { get; set; } = new();
```

and add after the `MonthTotals` class:

```csharp
/// <summary>
/// One column of the cash-flow timeline. Monday-start, because a week that
/// begins on Sunday puts two of a month's paydays in the same column.
/// </summary>
public sealed class WeekTotals
{
    /// <summary>Monday of the week, midnight UTC.</summary>
    public DateTime WeekStart { get; set; }

    public int Bills { get; set; }

    public decimal Paid { get; set; }

    public decimal Unpaid { get; set; }

    public decimal Total => Paid + Unpaid;
}
```

- [ ] **Step 4: Write the fold**

Create `BillsMinimalApi.Contracts/WeekBuckets.cs`:

```csharp
namespace BillsMinimalApi.Contracts;

/// <summary>
/// Folds one-row-per-due-date into one-row-per-week.
/// <para>
/// It lives in Contracts, not in the API, because Contracts is the only
/// assembly both the Blazor app and the unit-test project reference — and
/// because it is arithmetic with no database in it, which is exactly the kind of
/// thing the integration suite should not have to boot a container to check.
/// </para>
/// </summary>
public static class WeekBuckets
{
    /// <summary>One distinct due date and what falls on it.</summary>
    public readonly record struct DayTotals(
        DateTime Day, int Bills, decimal Paid, decimal Unpaid);

    /// <summary>
    /// Monday of the week <paramref name="day"/> falls in. The +6 %7 shift is
    /// there because <see cref="DayOfWeek"/> numbers Sunday as 0.
    /// </summary>
    public static DateTime StartOfWeek(DateTime day) =>
        day.Date.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static List<WeekTotals> FromDays(IEnumerable<DayTotals> days, int maxWeeks)
    {
        var byWeek = new Dictionary<DateTime, WeekTotals>();

        foreach (var day in days)
        {
            var start = StartOfWeek(day.Day);

            if (!byWeek.TryGetValue(start, out var week))
            {
                week = new WeekTotals { WeekStart = start };
                byWeek[start] = week;
            }

            week.Bills += day.Bills;
            week.Paid += day.Paid;
            week.Unpaid += day.Unpaid;
        }

        if (byWeek.Count == 0)
        {
            return new List<WeekTotals>();
        }

        var first = byWeek.Keys.Min();
        var last = byWeek.Keys.Max();
        var span = ((last - first).Days / 7) + 1;

        if (span > maxWeeks)
        {
            // Complete, but no longer continuous. Every week holding a bill is
            // still here; only the empty ones between them are gone.
            return byWeek.Values.OrderBy(w => w.WeekStart).ToList();
        }

        var filled = new List<WeekTotals>(span);

        for (var start = first; start <= last; start = start.AddDays(7))
        {
            filled.Add(byWeek.TryGetValue(start, out var week)
                ? week
                : new WeekTotals { WeekStart = start });
        }

        return filled;
    }
}
```

- [ ] **Step 5: Run the unit tests to verify they pass**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln --filter FullyQualifiedName~WeekBucketTests`
Expected: PASS, 9 tests.

- [ ] **Step 6: Write the failing integration test**

Create `tests/BillsMinimalApi.Tests/BillTimelineTests.cs`:

```csharp
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
```

- [ ] **Step 7: Run it to verify it fails**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln --filter FullyQualifiedName~BillTimelineTests`
Expected: FAIL — `Assert.Single() Failure: The collection was empty`, because nothing populates `Weeks` yet.

- [ ] **Step 8: Build the aggregate**

In `BillsMinimalApi/Queries/BillSummaryBuilder.cs`, add the call to `BuildAsync` immediately after the `summary.Months = …` line:

```csharp
        summary.Weeks = await WeeksAsync(scoped, cancellationToken);
```

and add the method:

```csharp
    /// <summary>
    /// One row per week, paid and unpaid apart.
    /// <para>
    /// Grouped by due date in Postgres and folded into weeks in memory.
    /// <c>date_trunc('week', …)</c> has no dependable Npgsql translation, and
    /// grouping on the raw column needs none: due dates are stored at midnight
    /// UTC, so one group is one day. The fold that follows is pure arithmetic
    /// with a unit test of its own.
    /// </para>
    /// </summary>
    private static async Task<List<WeekTotals>> WeeksAsync(
        IQueryable<Bill> scoped,
        CancellationToken cancellationToken)
    {
        var rows = await scoped
            .GroupBy(b => b.DueDate)
            .Select(g => new
            {
                Day = g.Key,
                Bills = g.Count(),
                Paid = g.Sum(b => b.Paid ? b.PaymentDue : 0m),
                Unpaid = g.Sum(b => b.Paid ? 0m : b.PaymentDue),
            })
            .ToListAsync(cancellationToken);

        return WeekBuckets.FromDays(
            rows.Select(r => new WeekBuckets.DayTotals(r.Day, r.Bills, r.Paid, r.Unpaid)),
            BillSummary.MaxWeeks);
    }
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln --filter FullyQualifiedName~BillTimelineTests`
Expected: PASS, 4 tests.

- [ ] **Step 10: Commit**

```bash
git add BillsMinimalApi.Contracts/BillSummary.cs BillsMinimalApi.Contracts/WeekBuckets.cs BillsMinimalApi/Queries/BillSummaryBuilder.cs tests/BillsMinimalApi.UnitTests/WeekBucketTests.cs tests/BillsMinimalApi.Tests/BillTimelineTests.cs
git commit -m "Total the book by the week each bill falls due"
```

---

### Task 4: The full late list and the age of the worst one

`Priority` holds six bills and mixes late with merely-due-soon. The Overview needs every late bill, oldest first, and the headline sentence needs the age of the oldest ("the oldest by 156 days").

**Files:**
- Modify: `BillsMinimalApi.Contracts/BillSummary.cs` (add `LateCount`, `Late`, `OldestDaysLate`)
- Modify: `BillsMinimalApi/Queries/BillSummaryBuilder.cs` (add `LateAsync`, call it from `BuildAsync`)
- Test: `tests/BillsMinimalApi.Tests/BillLateListTests.cs`

**Interfaces:**
- Consumes: `SummaryBill`, `BillSummaryBuilder.ToSummaryBill(Bill, DateTime)` (both existing).
- Produces:
  - `BillSummary.Late : List<SummaryBill>` — unpaid and past due, oldest due date first.
  - `BillSummary.LateCount` = `200` — the cap on that list.
  - `BillSummary.OldestDaysLate : int` (get-only, derived).

- [ ] **Step 1: Write the failing test**

Create `tests/BillsMinimalApi.Tests/BillLateListTests.cs`:

```csharp
namespace BillsMinimalApi.Tests;

/// <summary>
/// <c>BillSummary.Late</c> and <c>OldestDaysLate</c> — the list the Overview
/// triages from and the number its headline sentence quotes.
/// <para>
/// Dates are relative to the real clock rather than fixed, because "late" is a
/// comparison the server makes against its own today and a hard-coded date would
/// mean something different every time the suite ran.
/// </para>
/// </summary>
public sealed class BillLateListTests(PostgresApiFixture fixture) : ApiTestBase(fixture)
{
    private static DateTime DaysAgo(int days) => DateTime.UtcNow.Date.AddDays(-days);

    [Fact]
    public async Task The_late_list_is_oldest_first_because_that_is_the_order_you_pay_in()
    {
        await Fixture.CreateBillAsync(payeeName: "Newer", dueDate: DaysAgo(3));
        await Fixture.CreateBillAsync(payeeName: "Older", dueDate: DaysAgo(40));

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(new[] { "Older", "Newer" }, summary.Late.Select(b => b.PayeeName));
        Assert.Equal(40, summary.Late[0].DaysLate);
    }

    [Fact]
    public async Task A_bill_that_is_paid_is_not_late_however_old_it_is()
    {
        await Fixture.CreateBillAsync(payeeName: "Settled", paid: true, dueDate: DaysAgo(90));
        await Fixture.CreateBillAsync(payeeName: "Outstanding", dueDate: DaysAgo(5));

        var summary = await Fixture.GetSummaryAsync();

        var late = Assert.Single(summary.Late);
        Assert.Equal("Outstanding", late.PayeeName);
    }

    [Fact]
    public async Task A_bill_not_yet_due_is_not_late_either()
    {
        await Fixture.CreateBillAsync(payeeName: "Upcoming", dueDate: DateTime.UtcNow.Date.AddDays(7));

        var summary = await Fixture.GetSummaryAsync();

        Assert.Empty(summary.Late);
    }

    [Fact]
    public async Task The_oldest_figure_is_the_top_of_the_list_and_not_a_second_query()
    {
        await Fixture.CreateBillAsync(dueDate: DaysAgo(12));
        await Fixture.CreateBillAsync(dueDate: DaysAgo(156));

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(156, summary.OldestDaysLate);
    }

    [Fact]
    public async Task Nothing_late_is_zero_days_rather_than_an_empty_headline()
    {
        // The Overview quotes this figure in a sentence, so it has to be a number
        // even on the happy day when nothing is late.
        await Fixture.CreateBillAsync(paid: true, dueDate: DaysAgo(30));

        var summary = await Fixture.GetSummaryAsync();

        Assert.Empty(summary.Late);
        Assert.Equal(0, summary.OldestDaysLate);
    }

    [Fact]
    public async Task The_list_and_the_overdue_headline_describe_the_same_bills()
    {
        // The Overview shows both at once; they came from two queries and must
        // still agree.
        await Fixture.CreateBillAsync(paymentDue: 100m, dueDate: DaysAgo(10));
        await Fixture.CreateBillAsync(paymentDue: 250m, dueDate: DaysAgo(20));
        await Fixture.CreateBillAsync(paymentDue: 999m, paid: true, dueDate: DaysAgo(20));

        var summary = await Fixture.GetSummaryAsync();

        Assert.Equal(summary.OverdueCount, summary.Late.Count);
        Assert.Equal(summary.OverdueAmount, summary.Late.Sum(b => b.PaymentDue));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln --filter FullyQualifiedName~BillLateListTests`
Expected: build error — `'BillSummary' does not contain a definition for 'Late'`.

- [ ] **Step 3: Add the contract**

In `BillsMinimalApi.Contracts/BillSummary.cs`, add after the `MaxWeeks` constant:

```csharp
    /// <summary>
    /// How many late bills the triage list carries. A list you work through, not
    /// a second bills table — and a bound on a response that would otherwise
    /// grow with the size of the problem. The Overview reports
    /// <see cref="OverdueCount"/> alongside it, so a truncated list says so.
    /// </summary>
    public const int LateCount = 200;
```

and after the `Priority` property:

```csharp
    /// <summary>
    /// Every unpaid bill already past due, oldest first — capped at
    /// <see cref="LateCount"/>. Distinct from <see cref="Priority"/>, which is a
    /// six-bill shortlist that also includes bills merely due soon.
    /// </summary>
    public List<SummaryBill> Late { get; set; } = new();

    /// <summary>
    /// How many days late the worst bill is, or 0 when nothing is late.
    /// <para>
    /// Derived rather than queried: <see cref="Late"/> is already ordered by due
    /// date, so its first row is the oldest one. A second query could disagree
    /// with the list it is printed next to.
    /// </para>
    /// </summary>
    public int OldestDaysLate => Late.Count == 0 ? 0 : Late[0].DaysLate;
```

- [ ] **Step 4: Build the aggregate**

In `BillsMinimalApi/Queries/BillSummaryBuilder.cs`, add to `BuildAsync` immediately after the `summary.Priority = …` line:

```csharp
        summary.Late = await LateAsync(scoped, today, cancellationToken);
```

and add the method:

```csharp
    /// <summary>
    /// Every unpaid bill already past due, oldest first.
    /// <para>
    /// The predicate is character for character the one
    /// <see cref="AddHeadlineAsync"/> counts with, because the Overview prints
    /// the count and the list side by side and a bill in one but not the other
    /// reads as a bug in both.
    /// </para>
    /// </summary>
    private static async Task<List<SummaryBill>> LateAsync(
        IQueryable<Bill> scoped,
        DateTime today,
        CancellationToken cancellationToken)
    {
        var rows = await scoped
            .Where(b => !b.Paid && b.DueDate < today)
            .OrderBy(b => b.DueDate)
            .ThenBy(b => b.Id)
            .Take(BillSummary.LateCount)
            .ToListAsync(cancellationToken);

        return rows.Select(b => ToSummaryBill(b, today)).ToList();
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln --filter FullyQualifiedName~BillLateListTests`
Expected: PASS, 6 tests.

Note on the cap: `LateCount` has no test of its own. Exercising it means arranging 201 bills over HTTP for a single assertion, and it is a backstop rather than behaviour the UI depends on — Task 8 renders "Showing the first 200 of N" from `OverdueCount` whenever the list is short of it, so a truncated list is visible rather than silent.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln`
Expected: PASS. The backend is now complete; every remaining task is frontend.

- [ ] **Step 7: Commit**

```bash
git add BillsMinimalApi.Contracts/BillSummary.cs BillsMinimalApi/Queries/BillSummaryBuilder.cs tests/BillsMinimalApi.Tests/BillLateListTests.cs
git commit -m "List every late bill, oldest first"
```

---

### Task 5: The token layer and the two theme toggles

Everything after this task styles against tokens, so it comes first. The four palette×mode blocks land, Bootstrap's own variables are bridged onto them so unrestyled components follow, and the theme is applied before first paint.

**Files:**
- Create: `bills-frontend/BillsFrontEndBlazor/wwwroot/css/tokens.css`
- Create: `bills-frontend/BillsFrontEndBlazor/wwwroot/js/theme.js`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/ThemeSwitcher.razor`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/ThemeSwitcher.razor.css`
- Modify: `bills-frontend/BillsFrontEndBlazor/Pages/_Layout.cshtml:6`, `:16-19`
- Modify: `bills-frontend/BillsFrontEndBlazor/Pages/_AccountLayout.cshtml:8`, `:16-18`
- Modify: `bills-frontend/BillsFrontEndBlazor/wwwroot/css/site.css:1-3`
- Modify: `bills-frontend/BillsFrontEndBlazor/Shared/NavMenu.razor:63` (drop the switcher in above the closing `</AuthorizeView>`)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - CSS custom properties on `:root`, readable from anywhere: `--bg --surface --sunken --text --muted --faint --border --accent --accent-text --late --ok --age-1 … --age-5 --radius --radius-lg --border-width --font-sans`.
  - `window.billsTheme.read() -> { palette, mode }`, `.apply(palette, mode)`, `.save(palette, mode)`.
  - `<ThemeSwitcher />` — no parameters.
  - `<html data-palette="nocturne|current" data-mode="light|dark" data-bs-theme="light|dark">`.

**Where the values come from.** Every hex in the Nocturne blocks, and the accents, lates, oks and aging ramps in the Current blocks, is verbatim from the handoff. The handoff leaves gaps — Nocturne light has no faint/border/late/ok, and the Current blocks give no text/muted/faint/sunken/border. Those are filled from the palette's own logic and marked with a comment in the file: Current takes Bootstrap 5's greys, which is what "matches today's app" means; Nocturne light takes its late from the dark end of its own aging ramp, the same relationship Nocturne dark has.

- [ ] **Step 1: Write the token sheet**

Create `bills-frontend/BillsFrontEndBlazor/wwwroot/css/tokens.css`:

```css
/* ---------------------------------------------------------------------------
   Design tokens.
   
   The one file in this app allowed to contain a hex value. Everything else
   refers to these names, which is what makes four palettes possible without
   four copies of the stylesheet.

   Two independent attributes, not one light/dark switch: Nocturne and Current
   are separate brand directions and each has its own light and dark variant.
   The attributes are written into the markup of both layouts, so the palette is
   right with JavaScript disabled; wwwroot/js/theme.js only corrects them to
   whatever the visitor last chose.
   --------------------------------------------------------------------------- */

:root {
    /* Shared shape. A 1px border is the only elevation cue in this design —
       there are no drop shadows anywhere, deliberately. */
    --radius: 8px;
    --radius-lg: 14px;
    --border-width: 1px;

    --font-sans: "Inter", system-ui, -apple-system, "Segoe UI", sans-serif;
}

:root[data-palette="current"] {
    --font-sans: "Helvetica Neue", Helvetica, Arial, sans-serif;
}

/* -- Nocturne, dark (the design's default) --------------------------------- */
:root[data-palette="nocturne"][data-mode="dark"] {
    --bg: #161826;
    --surface: #232532;
    --sunken: #1b1d2a;
    --text: #e9e9ed;
    --muted: #9397ab;
    --faint: #75798c;
    --border: #3f424d;

    /* Outline only. Nothing in Nocturne is ever flooded with the accent. */
    --accent: #9184d9;
    --accent-text: #d2cefd;

    --late: #b5abfc;
    --ok: #75798c;

    --age-1: #595d6c;
    --age-2: #5d5294;
    --age-3: #796cbf;
    --age-4: #968ae0;
    --age-5: #b5abfc;
}

/* -- Nocturne, light ------------------------------------------------------- */
:root[data-palette="nocturne"][data-mode="light"] {
    --bg: #f4f3f9;
    --surface: #ffffff;
    --sunken: #ece9f7;
    --text: #1e1c2e;
    --muted: #6b6880;

    /* Not in the handoff. faint and border sit between muted and sunken on the
       same violet cast; late is the dark end of this palette's own ramp, which
       is the relationship Nocturne dark has between its late and its ramp. */
    --faint: #8f8ba3;
    --border: #dcd8ec;
    --late: #4a3a9e;
    --ok: #6b6880;

    --accent: #7c6dd1;
    --accent-text: #5b4bc4;

    --age-1: #d9d5ec;
    --age-2: #b8b0dd;
    --age-3: #9184d9;
    --age-4: #6a5cc2;
    --age-5: #4a3a9e;
}

/* -- Current, light (today's app) ------------------------------------------ */
:root[data-palette="current"][data-mode="light"] {
    --bg: #f8f9fa;
    --surface: #ffffff;

    /* Not in the handoff: Bootstrap 5's own greys, which is what makes this
       palette match the app as it stands. */
    --sunken: #e9ecef;
    --text: #212529;
    --muted: #6c757d;
    --faint: #adb5bd;
    --border: #dee2e6;
    --accent-text: #0a58ca;

    --accent: #0d6efd;
    --late: #dc3545;
    --ok: #198754;

    --age-1: #adb5bd;
    --age-2: #ffc107;
    --age-3: #fd7e14;
    --age-4: #dc3545;
    --age-5: #842029;
}

/* -- Current, dark --------------------------------------------------------- */
:root[data-palette="current"][data-mode="dark"] {
    --bg: #17191c;
    --surface: #1f2226;

    /* Not in the handoff: Bootstrap 5's dark-mode greys, and a ramp that runs
       from neutral to this palette's own late colour — brighter reads as worse
       on a dark ground, which is the opposite of the light ramp. */
    --sunken: #212529;
    --text: #dee2e6;
    --muted: #adb5bd;
    --faint: #6c757d;
    --border: #343a40;
    --accent-text: #6ea8fe;

    --accent: #3d8bfd;
    --late: #ea868f;
    --ok: #75b798;

    --age-1: #495057;
    --age-2: #997404;
    --age-3: #ca6510;
    --age-4: #dc3545;
    --age-5: #ea868f;
}

/* ---------------------------------------------------------------------------
   Bootstrap bridge.

   Bootstrap 5.3 draws everything from its own custom properties, so pointing
   those at the tokens re-themes every component this app never restyled —
   modals, dropdowns, form controls, tables — instead of leaving them in the
   default light theme against a Nocturne background.

   The selector carries a data-palette so it outranks Bootstrap's own
   [data-bs-theme=dark] block, which is one attribute where this is two.
   --------------------------------------------------------------------------- */
:root[data-palette] {
    --bs-body-bg: var(--bg);
    --bs-body-color: var(--text);
    --bs-body-font-family: var(--font-sans);
    --bs-secondary-color: var(--muted);
    --bs-tertiary-color: var(--faint);
    --bs-border-color: var(--border);
    --bs-border-radius: var(--radius);
    --bs-border-radius-lg: var(--radius-lg);
    --bs-primary: var(--accent);
    --bs-link-color: var(--accent-text);
    --bs-link-hover-color: var(--accent);
    --bs-emphasis-color: var(--text);
}

body {
    background-color: var(--bg);
    color: var(--text);
    font-family: var(--font-sans);
}
```

- [ ] **Step 2: Write the boot script**

Create `bills-frontend/BillsFrontEndBlazor/wwwroot/js/theme.js`:

```javascript
// Applied before first paint, from a blocking <script> in <head>.
//
// This cannot be done from Blazor. The app renders ServerPrerendered, so
// IJSRuntime is unusable until OnAfterRenderAsync — by which time the page has
// already painted, and every load would flash the default theme before
// switching to the chosen one.
//
// The same file also owns the two localStorage keys, so ThemeSwitcher.razor
// never has to know what they are called.
(function () {
    'use strict';

    var PALETTE_KEY = 'bills.palette';
    var MODE_KEY = 'bills.mode';
    var PALETTES = ['nocturne', 'current'];
    var MODES = ['light', 'dark'];

    function readStored(key, allowed, fallback) {
        try {
            var value = window.localStorage.getItem(key);
            return allowed.indexOf(value) === -1 ? fallback : value;
        } catch (e) {
            // Private browsing and blocked storage both throw on getItem. A
            // theme is a preference, not a feature: falling back is the whole
            // of the handling this deserves.
            return fallback;
        }
    }

    function apply(palette, mode) {
        var root = document.documentElement;
        root.setAttribute('data-palette', palette);
        root.setAttribute('data-mode', mode);

        // Bootstrap 5.3 reads this one, and mirroring it is what makes modals,
        // dropdowns and form controls follow the mode without being restyled.
        root.setAttribute('data-bs-theme', mode);
    }

    function read() {
        return {
            palette: readStored(PALETTE_KEY, PALETTES, 'nocturne'),
            mode: readStored(MODE_KEY, MODES, 'dark')
        };
    }

    function save(palette, mode) {
        try {
            window.localStorage.setItem(PALETTE_KEY, palette);
            window.localStorage.setItem(MODE_KEY, mode);
        } catch (e) {
            // See readStored. The choice still applies for this page.
        }

        apply(palette, mode);
    }

    var chosen = read();
    apply(chosen.palette, chosen.mode);

    window.billsTheme = { apply: apply, read: read, save: save };
})();
```

- [ ] **Step 3: Wire both layouts**

In `bills-frontend/BillsFrontEndBlazor/Pages/_Layout.cshtml`, replace line 6:

```html
<html lang="en" data-palette="nocturne" data-mode="dark" data-bs-theme="dark">
```

and replace the stylesheet block (lines 16–19) with:

```html
    @* theme.js is blocking and first: it sets data-palette and data-mode before
       anything paints. The attributes are in the markup above as well, so the
       page is Nocturne dark rather than unthemed when scripting is off. *@
    <script src="js/theme.js"></script>

    <link rel="stylesheet" href="css/bootstrap/bootstrap.min.css" />
    <link rel="stylesheet" href="css/bootstrap-icons/bootstrap-icons.min.css" />
    <link href="css/tokens.css" rel="stylesheet" />
    <link href="css/site.css" rel="stylesheet" />
    <link href="BillsFrontEndBlazor.styles.css" rel="stylesheet" />
```

In `bills-frontend/BillsFrontEndBlazor/Pages/_AccountLayout.cshtml`, replace line 8:

```html
<html lang="en" data-palette="nocturne" data-mode="dark" data-bs-theme="dark">
```

and replace the stylesheet block (lines 16–18) with:

```html
    @* The same boot script as _Layout. Without it the login screen is the one
       page in the app that ignores the visitor's theme — and it is the first
       page most of them see. There is no ThemeSwitcher here because there is no
       Blazor here; the choice is made inside the app and read back out. *@
    <script src="js/theme.js"></script>

    <link rel="stylesheet" href="css/bootstrap/bootstrap.min.css" />
    <link rel="stylesheet" href="css/bootstrap-icons/bootstrap-icons.min.css" />
    <link href="css/tokens.css" rel="stylesheet" />
    <link href="css/site.css" rel="stylesheet" />
```

- [ ] **Step 4: Let the font token win**

In `bills-frontend/BillsFrontEndBlazor/wwwroot/css/site.css`, replace lines 1–3:

```css
html, body {
    font-family: var(--font-sans);
}
```

Inter is not vendored until Task 6; until then the token's fallback stack renders and nothing breaks.

- [ ] **Step 5: Write the switcher**

Create `bills-frontend/BillsFrontEndBlazor/Shared/ThemeSwitcher.razor`:

```razor
@inject IJSRuntime JS

@* Two independent controls, not one switch. Palette and mode are separate
   choices in this design and collapsing them would make three of the four
   combinations unreachable. *@
<div class="theme-switcher">

    <div class="theme-palette" role="group" aria-label="Palette">
        <button type="button" class="theme-option @(_palette == "nocturne" ? "on" : null)"
                @onclick="@(() => ChooseAsync("nocturne", _mode))">
            Nocturne
        </button>
        <button type="button" class="theme-option @(_palette == "current" ? "on" : null)"
                @onclick="@(() => ChooseAsync("current", _mode))">
            Current
        </button>
    </div>

    <button type="button" class="theme-mode" title="@ModeLabel" aria-label="@ModeLabel"
            @onclick="ToggleModeAsync">
        <i class="bi @(_mode == "dark" ? "bi-sun-fill" : "bi-moon-stars-fill")"></i>
    </button>

</div>

@code {
    // Seeded with the same defaults the markup and the boot script use, so the
    // prerendered buttons are not briefly wrong for a visitor who never changed
    // anything.
    private string _palette = "nocturne";
    private string _mode = "dark";

    private string ModeLabel => _mode == "dark" ? "Switch to light mode" : "Switch to dark mode";

    /// <summary>Shape of what <c>billsTheme.read()</c> hands back.</summary>
    private sealed record ThemeChoice(string Palette, string Mode);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        // The first moment interop is available — the app prerenders on the
        // server, where IJSRuntime is not usable. The page is already wearing
        // the right theme by now; this only catches the buttons up with it.
        var chosen = await JS.InvokeAsync<ThemeChoice>("billsTheme.read");

        _palette = chosen.Palette;
        _mode = chosen.Mode;

        StateHasChanged();
    }

    private async Task ChooseAsync(string palette, string mode)
    {
        _palette = palette;
        _mode = mode;

        // The script persists and applies in one call, so the keys stay in one
        // place and the two can never drift apart.
        await JS.InvokeVoidAsync("billsTheme.save", palette, mode);
    }

    private Task ToggleModeAsync() => ChooseAsync(_palette, _mode == "dark" ? "light" : "dark");
}
```

Create `bills-frontend/BillsFrontEndBlazor/Shared/ThemeSwitcher.razor.css`:

```css
.theme-switcher {
    display: flex;
    align-items: center;
    gap: .5rem;
    padding: .5rem .75rem;
}

.theme-palette {
    display: flex;
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius);
    overflow: hidden;
}

.theme-option {
    background: none;
    border: 0;
    color: var(--muted);
    font-size: .75rem;
    padding: .25rem .5rem;
    cursor: pointer;
}

/* Outlined, never flooded — the accent is a border and a text colour in this
   design and nothing else. */
.theme-option.on {
    color: var(--accent-text);
    box-shadow: inset 0 0 0 var(--border-width) var(--accent);
}

.theme-mode {
    background: none;
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius);
    color: var(--muted);
    cursor: pointer;
    line-height: 1;
    padding: .3rem .45rem;
}

.theme-mode:hover,
.theme-option:hover {
    color: var(--text);
}
```

- [ ] **Step 6: Put the switcher in the sidebar**

In `bills-frontend/BillsFrontEndBlazor/Shared/NavMenu.razor`, insert immediately before the `<AuthorizeView>` element:

```razor
    <ThemeSwitcher />
```

- [ ] **Step 7: Verify it builds**

Run: `dotnet build BillsMinimalApi/BillsMinimalApi.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Verify it themes, by eye**

Run the app (`dotnet run --project bills-frontend/BillsFrontEndBlazor`), sign in, and check four things:
1. The page opens in Nocturne dark with no flash of light at any point during load.
2. All four combinations are reachable and each changes the page background, text and borders.
3. A hard reload keeps the last choice — again with no flash.
4. Signing out lands on the login screen wearing the same theme.

- [ ] **Step 9: Commit**

```bash
git add bills-frontend/BillsFrontEndBlazor/wwwroot/css/tokens.css bills-frontend/BillsFrontEndBlazor/wwwroot/js/theme.js bills-frontend/BillsFrontEndBlazor/Shared/ThemeSwitcher.razor bills-frontend/BillsFrontEndBlazor/Shared/ThemeSwitcher.razor.css bills-frontend/BillsFrontEndBlazor/Pages/_Layout.cshtml bills-frontend/BillsFrontEndBlazor/Pages/_AccountLayout.cshtml bills-frontend/BillsFrontEndBlazor/wwwroot/css/site.css bills-frontend/BillsFrontEndBlazor/Shared/NavMenu.razor
git commit -m "Give the app four palettes and no flash"
```

---

### Task 6: Phosphor in, Bootstrap Icons out

The spec calls for Phosphor regular throughout. This task vendors it and Inter, adds the wrapper component the later tasks use, mechanically renames all 44 icon classes already in the app, and deletes Bootstrap Icons once nothing points at it.

**Files:**
- Create: `bills-frontend/BillsFrontEndBlazor/wwwroot/css/phosphor/style.css` (downloaded)
- Create: `bills-frontend/BillsFrontEndBlazor/wwwroot/css/phosphor/Phosphor.woff2` (downloaded)
- Create: `bills-frontend/BillsFrontEndBlazor/wwwroot/css/phosphor/Phosphor.woff` (downloaded)
- Create: `bills-frontend/BillsFrontEndBlazor/wwwroot/css/fonts/inter/inter-latin-wght-normal.woff2` (downloaded)
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/Icon.razor`
- Modify: `bills-frontend/BillsFrontEndBlazor/wwwroot/css/tokens.css` (prepend the Inter `@font-face`)
- Modify: `bills-frontend/BillsFrontEndBlazor/Pages/_Layout.cshtml`, `Pages/_AccountLayout.cshtml` (swap the stylesheet link)
- Modify: every `.razor`, `.razor.cs` and `.cshtml` under `Pages/` and `Shared/` that names an icon (the sweep in Step 4 finds them)
- Delete: `bills-frontend/BillsFrontEndBlazor/wwwroot/css/bootstrap-icons/` (the `.min.css` and the two font files)

**Interfaces:**
- Consumes: `--font-sans` from Task 5.
- Produces: `<Icon Name="receipt" Size="18" Class="me-1" />` — `Name` is the Phosphor glyph without the `ph-` prefix, `Size` is px (default 16), `Class` is optional extra classes.

- [ ] **Step 1: Vendor Phosphor**

Phosphor is a webfont, exactly like the Bootstrap Icons it replaces, so it vendors into the same shape: one stylesheet beside its font files.

```bash
cd bills-frontend/BillsFrontEndBlazor/wwwroot/css
PH=https://cdn.jsdelivr.net/npm/@phosphor-icons/web@2/src/regular
curl -fL --create-dirs -o phosphor/style.css      "$PH/style.css"
curl -fL --create-dirs -o phosphor/Phosphor.woff2 "$PH/Phosphor.woff2"
curl -fL --create-dirs -o phosphor/Phosphor.woff  "$PH/Phosphor.woff"
cd -
```

Expected sizes: `style.css` ~78 KB, `Phosphor.woff2` ~147 KB, `Phosphor.woff` ~489 KB.

The stylesheet's `@font-face` also lists a `.ttf` and a `.svg`, which are not being vendored — no browser this app supports would ever reach past `woff2`, and a hermetic build should not name files it does not ship. Delete those two lines from `wwwroot/css/phosphor/style.css`, leaving:

```css
@font-face {
  font-family: "Phosphor";
  src:
    url("./Phosphor.woff2") format("woff2"),
    url("./Phosphor.woff") format("woff");
  font-weight: normal;
  font-style: normal;
  font-display: block;
}
```

Verify the download is real rather than a CDN error page:

```bash
grep -c '^\.ph\.ph-receipt:before' bills-frontend/BillsFrontEndBlazor/wwwroot/css/phosphor/style.css
```

Expected: `1`.

- [ ] **Step 2: Vendor Inter and declare it**

Task 5's `--font-sans` names Inter and has been falling back to the system stack ever since. This is where it starts being true.

```bash
curl -fL --create-dirs \
  -o bills-frontend/BillsFrontEndBlazor/wwwroot/css/fonts/inter/inter-latin-wght-normal.woff2 \
  "https://cdn.jsdelivr.net/npm/@fontsource-variable/inter@5/files/inter-latin-wght-normal.woff2"
```

Expected size: ~48 KB.

Prepend to `bills-frontend/BillsFrontEndBlazor/wwwroot/css/tokens.css`, above the existing header comment:

```css
/* One variable font covers every weight this design uses, which is why there is
   a single file here and not the six a static family would need.
   
   Latin only. Anything outside that range falls through to the next family in
   --font-sans, per-glyph, which is what unicode-range buys.
   
   font-display: swap — text is readable in the fallback while the font loads
   rather than invisible, and Inter and the system stack have close enough
   metrics that the swap is not a jolt. */
@font-face {
    font-family: "Inter";
    src: url("fonts/inter/inter-latin-wght-normal.woff2") format("woff2-variations");
    font-weight: 100 900;
    font-style: normal;
    font-display: swap;
    unicode-range: U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA,
                   U+02DC, U+0304, U+0308, U+0329, U+2000-206F, U+2074, U+20AC,
                   U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD;
}
```

- [ ] **Step 3: Write the wrapper**

Create `bills-frontend/BillsFrontEndBlazor/Shared/Icon.razor`:

```razor
@* Phosphor is a font, so an icon is an <i> carrying two classes: the family and
   the glyph. Wrapping that here means the convention lives in one file instead
   of forty call sites — and the last icon-set swap had to touch all forty.

   aria-hidden on every icon: each one in this app sits beside its own label, so
   announcing it would just read the label twice. *@
<i class="ph ph-@Name @Class" style="font-size: @(Size)px" aria-hidden="true"></i>

@code {
    /// <summary>Phosphor glyph name without the <c>ph-</c> prefix, e.g. "receipt".</summary>
    [Parameter, EditorRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>Rendered size in px. The design uses 16–20 inline with text.</summary>
    [Parameter]
    public int Size { get; set; } = 16;

    /// <summary>Extra classes, for spacing utilities at the call site.</summary>
    [Parameter]
    public string? Class { get; set; }
}
```

- [ ] **Step 4: Rename every icon class**

Run this from the repository root. It rewrites `.razor`, `.razor.cs` and `.cshtml` files under `Pages/` and `Shared/`:

```bash
#!/usr/bin/env bash
set -euo pipefail
cd bills-frontend/BillsFrontEndBlazor

# Every bi-* class in the app, paired with its Phosphor equivalent.
#
# Bootstrap's four -fill variants map onto Phosphor's regular weight rather than
# pulling in a second 147 KB font for four icons; the spec asks for regular
# throughout. Phosphor's chevrons are called carets, its inbox is a tray, and its
# speedometer is a gauge — the names differ more than the drawings do.
MAP=$(cat <<'PAIRS'
bi-arrow-down-up ph-arrows-down-up
bi-arrow-repeat ph-arrows-clockwise
bi-box-arrow-in-right ph-sign-in
bi-box-arrow-right ph-sign-out
bi-caret-down-fill ph-caret-down
bi-caret-up-fill ph-caret-up
bi-cash-coin ph-coins
bi-check-circle-fill ph-check-circle
bi-check-circle ph-check-circle
bi-chevron-double-left ph-caret-double-left
bi-chevron-double-right ph-caret-double-right
bi-chevron-down ph-caret-down
bi-chevron-left ph-caret-left
bi-chevron-right ph-caret-right
bi-chevron-up ph-caret-up
bi-download ph-download-simple
bi-envelope ph-envelope-simple
bi-exclamation-octagon-fill ph-warning-octagon
bi-exclamation-triangle-fill ph-warning
bi-exclamation-triangle ph-warning
bi-graph-up-arrow ph-chart-line-up
bi-graph-up ph-chart-line
bi-inbox ph-tray
bi-info-circle-fill ph-info
bi-info-circle ph-info
bi-list ph-list
bi-lock-fill ph-lock-key
bi-lock ph-lock-simple
bi-moon-stars-fill ph-moon-stars
bi-pencil-square ph-note-pencil
bi-pencil ph-pencil-simple
bi-person-circle ph-user-circle
bi-person-plus ph-user-plus
bi-plus-circle ph-plus-circle
bi-plus-lg ph-plus
bi-receipt-cutoff ph-receipt
bi-receipt ph-receipt
bi-search ph-magnifying-glass
bi-sort-down ph-sort-descending
bi-sort-up ph-sort-ascending
bi-speedometer2 ph-gauge
bi-sun-fill ph-sun
bi-table ph-table
bi-trash ph-trash
PAIRS
)

# Longest source name first. "bi-check-circle" is a prefix of
# "bi-check-circle-fill", so doing the short one first would leave
# "ph-check-circle-fill" — a class that does not exist and renders as nothing.
# Sorting by length removes the hazard instead of trusting the order above.
while read -r from to; do
    [ -z "$from" ] && continue
    printf '%s %s %s\n' "${#from}" "$from" "$to"
done <<< "$MAP" | sort -rn | while read -r _len from to; do
    find Pages Shared -type f \
        \( -name '*.razor' -o -name '*.cs' -o -name '*.cshtml' \) \
        -exec perl -pi -e "s/\Q$from\E/$to/g" {} +
done

# The family class. Bootstrap Icons matched on the bi- prefix alone, so bare "bi"
# was decorative; Phosphor's selector is .ph.ph-name, so "ph" is load-bearing.
# Safe only now that no bi- names survive the pass above.
find Pages Shared -type f \
    \( -name '*.razor' -o -name '*.cs' -o -name '*.cshtml' \) \
    -exec perl -pi -e 's/class="bi /class="ph /g' {} +
```

- [ ] **Step 5: Verify nothing names Bootstrap Icons any more**

```bash
grep -rn 'bi-\|class="bi ' bills-frontend/BillsFrontEndBlazor/Pages bills-frontend/BillsFrontEndBlazor/Shared
```

Expected: no output (exit 1). If anything is listed, it is an icon the map above missed — add the pair and re-run Step 4.

- [ ] **Step 6: Swap the stylesheet and delete the old font**

In both `bills-frontend/BillsFrontEndBlazor/Pages/_Layout.cshtml` and `Pages/_AccountLayout.cshtml`, replace:

```html
    <link rel="stylesheet" href="css/bootstrap-icons/bootstrap-icons.min.css" />
```

with:

```html
    <link rel="stylesheet" href="css/phosphor/style.css" />
```

Then remove the vendored font it pointed at:

```bash
git rm -r bills-frontend/BillsFrontEndBlazor/wwwroot/css/bootstrap-icons
```

- [ ] **Step 7: Verify it builds**

Run: `dotnet build BillsMinimalApi/BillsMinimalApi.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Verify the icons drew, by eye**

Run the app and check that no icon renders as a blank box or a tofu square on: the login screen, the sidebar, Overview, Bills (including the sort carets in the table headers — click a column to flip them), Reports, and a toast (delete a bill).

The sort carets and the toast icons are worth the extra click: those names come from C# expressions rather than markup, so a miss there is invisible until the state that produces it happens.

- [ ] **Step 9: Commit**

```bash
git add -A bills-frontend/BillsFrontEndBlazor
git commit -m "Trade Bootstrap Icons for Phosphor"
```

---

### Task 7: The obligation headline and the aging strip

Ideas 1 and 4. Both are pure functions of a `BillSummary`, so the arithmetic and the wording go in `BillsMinimalApi.Contracts` where they can be tested without a renderer, and the components stay markup.

**Files:**
- Create: `BillsMinimalApi.Contracts/ObligationSentence.cs`
- Create: `BillsMinimalApi.Contracts/StackedStrip.cs`
- Create: `tests/BillsMinimalApi.UnitTests/ObligationSentenceTests.cs`
- Create: `tests/BillsMinimalApi.UnitTests/StackedStripTests.cs`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/ObligationHeadline.razor`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/ObligationHeadline.razor.css`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/AgingStrip.razor`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/AgingStrip.razor.css`

**Interfaces:**
- Consumes: `BillSummary.OldestDaysLate` (Task 4), `<Icon>` (Task 6), the `--age-*` tokens (Task 5).
- Produces:
  - `ObligationSentence.Describe(BillSummary summary, IFormatProvider? formatProvider = null) -> string`
  - `readonly record struct StripSegment(string Label, int Count, decimal Amount, double Percent)`
  - `StackedStrip.FromAging(IEnumerable<AgingBucket> buckets) -> List<StripSegment>`
  - `<ObligationHeadline Summary="@summary" />`, `<AgingStrip Buckets="@summary.Aging" />`
  - The headline's primary button links to `bills?filter=overdue`. **Task 9 must make `Bills.razor` read that query parameter** — the deep-link is what makes the sentence's count and the Bills page's Late group name the same bills.

- [ ] **Step 1: Write the failing sentence tests**

Create `tests/BillsMinimalApi.UnitTests/ObligationSentenceTests.cs`:

```csharp
using System.Globalization;
using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// The one sentence the Overview leads with. It is prose assembled from five
/// figures, and every branch of it is reachable from real data — an account with
/// nothing late, an account with nothing at all, an account with one bill one day
/// overdue.
/// <para>
/// The culture is passed in rather than read from the thread. The app pins en-US
/// in Program.cs, but a unit test has no Program.cs, and a suite that passes only
/// on a machine set to dollars is not a test of the sentence.
/// </para>
/// </summary>
public sealed class ObligationSentenceTests
{
    private static readonly CultureInfo Money = CultureInfo.GetCultureInfo("en-US");

    private static string Describe(BillSummary summary) =>
        ObligationSentence.Describe(summary, Money);

    [Fact]
    public void The_headline_reads_as_the_design_wrote_it()
    {
        // The handoff's own figures, which are cross-checked against the app's
        // Reports screen — so this is the sentence a real account produces.
        var summary = new BillSummary
        {
            TotalBilled = 6_108.50m,
            PaidAmount = 4_419.52m,
            OverdueAmount = 1_398.99m,
            OverdueCount = 8,
            DueSoonAmount = 289.99m,
            Late = { new SummaryBill { DaysLate = 156 } },
        };

        Assert.Equal(
            "$1,398.99 of it is already late, spread across 8 bills — the oldest by 156 days. "
            + "The rest, $289.99, falls due inside the next 30 days.",
            Describe(summary));
    }

    [Fact]
    public void A_settled_account_gets_a_sentence_rather_than_an_empty_one()
    {
        var summary = new BillSummary { TotalBilled = 900m, PaidAmount = 900m };

        Assert.Equal("Nothing outstanding — every bill on the books is paid.", Describe(summary));
    }

    [Fact]
    public void One_late_bill_is_a_bill_and_not_bills()
    {
        var summary = new BillSummary
        {
            TotalBilled = 50m,
            OverdueAmount = 50m,
            OverdueCount = 1,
            Late = { new SummaryBill { DaysLate = 4 } },
        };

        Assert.Contains("spread across 1 bill —", Describe(summary));
    }

    [Fact]
    public void One_day_late_is_a_day_and_not_days()
    {
        var summary = new BillSummary
        {
            TotalBilled = 50m,
            OverdueAmount = 50m,
            OverdueCount = 1,
            Late = { new SummaryBill { DaysLate = 1 } },
        };

        Assert.Contains("the oldest by 1 day.", Describe(summary));
    }

    [Fact]
    public void Nothing_late_says_so_instead_of_naming_a_zero()
    {
        var summary = new BillSummary { TotalBilled = 400m, DueSoonAmount = 400m };

        Assert.Equal(
            "None of it is late. $400.00 falls due inside the next 30 days.",
            Describe(summary));
    }

    [Fact]
    public void Nothing_due_soon_says_so_too()
    {
        // Everything outstanding sits further out than the window. The sentence
        // still has to end, and "and nothing is coming up either" is the news.
        var summary = new BillSummary { TotalBilled = 400m };

        Assert.Equal(
            "None of it is late. Nothing falls due inside the next 30 days.",
            Describe(summary));
    }

    [Fact]
    public void Money_outside_both_windows_is_never_called_the_rest()
    {
        // $1,000 outstanding, $200 late, $300 due inside 30 days — and $500 due
        // later. Calling the $300 "the rest" would be a lie the reader can check
        // against the total printed directly above it.
        var summary = new BillSummary
        {
            TotalBilled = 1_000m,
            OverdueAmount = 200m,
            OverdueCount = 2,
            DueSoonAmount = 300m,
            Late = { new SummaryBill { DaysLate = 9 } },
        };

        Assert.Equal(
            "$200.00 of it is already late, spread across 2 bills — the oldest by 9 days. "
            + "$300.00 of the remainder falls due inside the next 30 days.",
            Describe(summary));
    }

    [Fact]
    public void The_oldest_comes_from_the_late_list_and_not_from_a_second_figure()
    {
        // Late is ordered oldest first by the builder, so the head of it is the
        // answer. Reading it here rather than sending a separate number is what
        // stops the sentence disagreeing with the list underneath it.
        var summary = new BillSummary
        {
            TotalBilled = 300m,
            OverdueAmount = 300m,
            OverdueCount = 3,
            Late =
            {
                new SummaryBill { DaysLate = 90 },
                new SummaryBill { DaysLate = 12 },
                new SummaryBill { DaysLate = 2 },
            },
        };

        Assert.Contains("the oldest by 90 days.", Describe(summary));
    }
}
```

- [ ] **Step 2: Write the failing strip tests**

Create `tests/BillsMinimalApi.UnitTests/StackedStripTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run both to verify they fail**

Run: `dotnet test tests/BillsMinimalApi.UnitTests`
Expected: FAIL — `The name 'ObligationSentence' does not exist` and `The name 'StackedStrip' does not exist`.

- [ ] **Step 4: Write the sentence**

Create `BillsMinimalApi.Contracts/ObligationSentence.cs`:

```csharp
using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>
/// The sentence the Overview leads with, built from figures the server already
/// sends.
/// <para>
/// It lives here rather than in the component because it is a decision tree over
/// five numbers with pluralisation in it, and a decision tree in markup is a
/// decision tree nobody can test. This assembly is the only one both the Blazor
/// app and the unit tests reference, which is what makes that possible.
/// </para>
/// </summary>
public static class ObligationSentence
{
    /// <param name="formatProvider">
    /// How to spell money. Defaults to the current culture, which the app pins to
    /// en-US in <c>Program.cs</c>; tests pass it explicitly so they do not depend
    /// on the machine they run on.
    /// </param>
    public static string Describe(BillSummary summary, IFormatProvider? formatProvider = null)
    {
        var money = formatProvider ?? CultureInfo.CurrentCulture;

        if (summary.OutstandingAmount <= 0)
        {
            return "Nothing outstanding — every bill on the books is paid.";
        }

        var late = summary.OverdueAmount > 0
            ? string.Format(
                money,
                "{0:C} of it is already late, spread across {1} — the oldest by {2}.",
                summary.OverdueAmount,
                Bills(summary.OverdueCount),
                Days(summary.OldestDaysLate))
            : "None of it is late.";

        return $"{late} {Coming(summary, money)}";
    }

    private static string Coming(BillSummary summary, IFormatProvider money)
    {
        if (summary.DueSoonAmount <= 0)
        {
            return $"Nothing falls due inside the next {BillSummary.DueSoonDays} days.";
        }

        // "The rest" is a claim about the total printed directly above this
        // sentence, so it is only made when the two windows actually account for
        // all of it. Anything due beyond the window makes it false.
        if (summary.OverdueAmount > 0)
        {
            var coversEverything =
                summary.OverdueAmount + summary.DueSoonAmount == summary.OutstandingAmount;

            return string.Format(
                money,
                coversEverything
                    ? "The rest, {0:C}, falls due inside the next {1} days."
                    : "{0:C} of the remainder falls due inside the next {1} days.",
                summary.DueSoonAmount,
                BillSummary.DueSoonDays);
        }

        // Nothing is late, so the due-soon money is not "the rest" of anything —
        // it is simply what is coming.
        return string.Format(
            money,
            "{0:C} falls due inside the next {1} days.",
            summary.DueSoonAmount,
            BillSummary.DueSoonDays);
    }

    private static string Bills(int count) => count == 1 ? "1 bill" : $"{count} bills";

    private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";
}
```

- [ ] **Step 5: Write the strip**

Create `BillsMinimalApi.Contracts/StackedStrip.cs`:

```csharp
namespace BillsMinimalApi.Contracts;

/// <summary>One band of a stacked strip: what it is, and how wide.</summary>
public readonly record struct StripSegment(string Label, int Count, decimal Amount, double Percent);

/// <summary>
/// Collapses a set of labelled amounts into shares of their own total.
/// <para>
/// Every input row comes back, including the empty ones. The strip skips
/// zero-width bands because a band no pixels wide is not a band; the legend keeps
/// them, because "nothing is over 90 days late" is the best line on the page and
/// a legend that silently loses rows between loads cannot say it.
/// </para>
/// </summary>
public static class StackedStrip
{
    public static List<StripSegment> FromAging(IEnumerable<AgingBucket> buckets)
    {
        var rows = buckets as IReadOnlyList<AgingBucket> ?? buckets.ToList();
        var total = rows.Sum(b => b.Amount);

        return rows
            .Select(b => new StripSegment(
                b.Label,
                b.Count,
                b.Amount,
                // Divided in decimal and cast once, rather than cast twice and
                // divided in double: the shares of an odd total close on exactly
                // 100 this way, and the bar ends flush with its container.
                total == 0 ? 0 : (double)(b.Amount / total) * 100))
            .ToList();
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/BillsMinimalApi.UnitTests`
Expected: PASS, all tests green.

- [ ] **Step 7: Write the headline component**

Create `bills-frontend/BillsFrontEndBlazor/Shared/ObligationHeadline.razor`:

```razor
@using BillsMinimalApi.Contracts

@* Idea 1: one sentence in place of three counter cards and a marketing hero.
   The cards made the reader do the arithmetic; this does it for them. *@
<section class="obligation">

    <p class="eyebrow">WHAT YOU OWE</p>

    <p class="total">@Summary.OutstandingAmount.ToString("C")</p>

    <p class="sentence">@ObligationSentence.Describe(Summary)</p>

    <div class="actions">
        @if (Summary.OverdueCount > 0)
        {
            @* Navigates, and deliberately does not mark anything paid — the
               button that settles eight bills at once should be on the page
               showing those eight bills, not on a summary. *@
            <a class="action action-primary" href="bills?filter=overdue">
                <Icon Name="warning-octagon" Size="18" Class="me-1" />
                Clear the @Summary.OverdueCount late @(Summary.OverdueCount == 1 ? "bill" : "bills")
            </a>
        }

        <a class="action" href="bills">
            <Icon Name="receipt" Size="18" Class="me-1" />
            All bills
        </a>
    </div>

</section>

@code {
    [Parameter, EditorRequired]
    public BillSummary Summary { get; set; } = new();
}
```

Create `bills-frontend/BillsFrontEndBlazor/Shared/ObligationHeadline.razor.css`:

```css
.obligation {
    background: var(--surface);
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius-lg);
    padding: 1.75rem 2rem;
}

.eyebrow {
    color: var(--faint);
    font-size: .7rem;
    letter-spacing: .12em;
    margin: 0 0 .5rem;
}

.total {
    color: var(--text);
    font-size: 3rem;
    font-weight: 600;
    line-height: 1.1;
    margin: 0 0 .75rem;

    /* Tabular figures: the total is the largest thing on the page and the
       digits should not shuffle sideways when it changes. */
    font-variant-numeric: tabular-nums;
}

.sentence {
    color: var(--muted);
    margin: 0 0 1.25rem;
    max-width: 62ch;
}

.actions {
    display: flex;
    gap: .75rem;
}

.action {
    align-items: center;
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius);
    color: var(--muted);
    display: inline-flex;
    padding: .5rem .9rem;
    text-decoration: none;
}

/* Outline, not fill. In Nocturne the accent is never a background. */
.action-primary {
    border-color: var(--accent);
    color: var(--accent-text);
}

.action:hover {
    color: var(--text);
}
```

- [ ] **Step 8: Write the aging strip component**

Create `bills-frontend/BillsFrontEndBlazor/Shared/AgingStrip.razor`:

```razor
@using System.Globalization
@using BillsMinimalApi.Contracts

@* Idea 4: five rows of a table become one bar. The rows made every bucket look
   equally important; the bar shows which one holds the money. *@
<section class="aging">

    <h2 class="title">How late it is</h2>
    <p class="subtitle">Every unpaid bill, by age.</p>

    @if (_segments.Count == 0 || _segments.All(s => s.Percent == 0))
    {
        <p class="empty">Nothing unpaid.</p>
    }
    else
    {
        @* flex-grow rather than width percentages: the browser divides the
           remainder itself, so the bands always close flush on the right edge
           instead of leaving a rounding sliver. *@
        <div class="strip" role="img" aria-label="Unpaid bills by age">
            @foreach (var (segment, index) in _segments.Select((s, i) => (s, i)))
            {
                @if (segment.Percent > 0)
                {
                    @* title attribute, not a <title> element — that one is SVG
                       only, and this strip is divs. *@
                    <div class="band"
                         title="@($"{segment.Label}: {segment.Amount:C}")"
                         style="flex-grow: @Grow(segment.Percent); background: var(--age-@(index + 1))">
                    </div>
                }
            }
        </div>

        <ul class="legend">
            @foreach (var (segment, index) in _segments.Select((s, i) => (s, i)))
            {
                <li>
                    <span class="swatch" style="background: var(--age-@(index + 1))"></span>
                    <span class="label">@segment.Label</span>
                    <span class="figures">@segment.Count · @segment.Amount.ToString("C")</span>
                </li>
            }
        </ul>
    }

</section>

@code {
    [Parameter, EditorRequired]
    public List<AgingBucket> Buckets { get; set; } = new();

    private List<StripSegment> _segments = new();

    protected override void OnParametersSet() => _segments = StackedStrip.FromAging(Buckets);

    // Invariant, because this ends up inside a style attribute. A culture that
    // writes 12,5 would produce CSS the browser silently drops.
    private static string Grow(double percent) =>
        percent.ToString("0.####", CultureInfo.InvariantCulture);
}
```

Create `bills-frontend/BillsFrontEndBlazor/Shared/AgingStrip.razor.css`:

```css
.aging {
    background: var(--surface);
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius-lg);
    padding: 1.25rem 1.5rem;
}

.title {
    color: var(--text);
    font-size: 1rem;
    font-weight: 600;
    margin: 0;
}

.subtitle,
.empty {
    color: var(--muted);
    font-size: .85rem;
    margin: .15rem 0 1rem;
}

.strip {
    border-radius: var(--radius);
    display: flex;
    height: 14px;
    overflow: hidden;
    width: 100%;
}

.band {
    min-width: 2px;
}

.legend {
    display: grid;
    gap: .4rem 1.5rem;
    grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
    list-style: none;
    margin: 1rem 0 0;
    padding: 0;
}

.legend li {
    align-items: center;
    display: flex;
    font-size: .8rem;
    gap: .5rem;
}

.swatch {
    border-radius: 3px;
    display: inline-block;
    flex: none;
    height: 10px;
    width: 10px;
}

.label {
    color: var(--muted);
}

.figures {
    color: var(--text);
    font-variant-numeric: tabular-nums;
    margin-left: auto;
}
```

- [ ] **Step 9: Verify it builds**

Run: `dotnet build BillsMinimalApi/BillsMinimalApi.sln`
Expected: Build succeeded, 0 errors.

Nothing renders these two yet — Task 8 composes the Overview around them.

- [ ] **Step 10: Commit**

```bash
git add BillsMinimalApi.Contracts/ObligationSentence.cs BillsMinimalApi.Contracts/StackedStrip.cs tests/BillsMinimalApi.UnitTests/ObligationSentenceTests.cs tests/BillsMinimalApi.UnitTests/StackedStripTests.cs bills-frontend/BillsFrontEndBlazor/Shared/ObligationHeadline.razor bills-frontend/BillsFrontEndBlazor/Shared/ObligationHeadline.razor.css bills-frontend/BillsFrontEndBlazor/Shared/AgingStrip.razor bills-frontend/BillsFrontEndBlazor/Shared/AgingStrip.razor.css
git commit -m "Say what is owed in a sentence, and how late in one bar"
```

---

### Task 8: The cash-flow timeline, the late list, and the new Overview

Ideas 2 and 3, and the page that ties all four together. The old Overview — hero banner, three counter cards, donut, six-month bars, three action tiles — is entirely replaced.

**Files:**
- Create: `BillsMinimalApi.Contracts/TimelineLayout.cs`
- Create: `tests/BillsMinimalApi.UnitTests/TimelineLayoutTests.cs`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/CashFlowTimeline.razor`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/CashFlowTimeline.razor.css`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/LateBillsList.razor`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/LateBillsList.razor.css`
- Modify: `bills-frontend/BillsFrontEndBlazor/Services/BillService.cs` (add `MarkPaidAsync` after `DeleteBillAsync`, line 221)
- Rewrite: `bills-frontend/BillsFrontEndBlazor/Pages/Index.razor`
- Rewrite: `bills-frontend/BillsFrontEndBlazor/Pages/Index.razor.cs`
- Create: `bills-frontend/BillsFrontEndBlazor/Pages/Index.razor.css`

**Interfaces:**
- Consumes: `BillSummary.Weeks` / `WeekTotals` (Task 3), `BillSummary.Late` / `OldestDaysLate` (Task 4), `WeekBuckets.StartOfWeek` (Task 3), `<ObligationHeadline>` / `<AgingStrip>` (Task 7), `<Icon>` (Task 6).
- Produces:
  - `TimelineLayout.Build(IReadOnlyList<WeekTotals> weeks, DateTime today) -> TimelineLayout`
  - `TimelineLayout.NiceAxisMax(decimal max) -> decimal`
  - `BillService.MarkPaidAsync(long id, CancellationToken ct = default) -> Task<BillWriteResult>` — **Task 10 does not use this**; the Bills page already holds `Bill` rows with their concurrency tokens and writes them directly.
  - `<CashFlowTimeline Weeks="@..." Today="@..." />`, `<LateBillsList Bills="@..." OverdueCount="@..." OverdueAmount="@..." BusyId="@..." OnMarkPaid="@..." />`

- [ ] **Step 1: Write the failing layout tests**

Create `tests/BillsMinimalApi.UnitTests/TimelineLayoutTests.cs`:

```csharp
using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// The geometry behind the weekly timeline. It is arithmetic, and it is the part
/// that goes wrong silently — a bar drawn past its baseline or a "now" marker in
/// the wrong week looks like a rendering quirk rather than a bug, so it gets
/// tested away from the renderer.
/// </summary>
public sealed class TimelineLayoutTests
{
    // A Monday, so a week that starts here is a week the bucketing agrees with.
    private static readonly DateTime Monday = new(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc);

    private static WeekTotals Week(DateTime start, decimal paid, decimal unpaid, int bills = 1) =>
        new() { WeekStart = start, Bills = bills, Paid = paid, Unpaid = unpaid };

    [Fact]
    public void Paid_sits_on_the_baseline_and_unpaid_stacks_on_top_of_it()
    {
        // Settled money is the foundation of the bar: it is not going to move.
        var layout = TimelineLayout.Build(new[] { Week(Monday, paid: 250m, unpaid: 750m) }, Monday);
        var bar = layout.Bars[0];

        Assert.Equal(TimelineLayout.Baseline, bar.PaidY + bar.PaidHeight, 6);
        Assert.Equal(bar.PaidY, bar.UnpaidY + bar.UnpaidHeight, 6);
    }

    [Fact]
    public void A_week_at_the_axis_maximum_reaches_the_top_of_the_plot()
    {
        // $1,000 rounds to an axis of exactly $1,000, so this stack is full
        // height — and full height must land on the ceiling, not through it.
        var layout = TimelineLayout.Build(new[] { Week(Monday, paid: 250m, unpaid: 750m) }, Monday);

        Assert.Equal(1_000m, layout.AxisMax);
        Assert.Equal(TimelineLayout.PlotTop, layout.Bars[0].UnpaidY, 6);
    }

    [Fact]
    public void Weeks_keep_their_order_and_share_a_width()
    {
        var layout = TimelineLayout.Build(
            new[]
            {
                Week(Monday, 100m, 0m),
                Week(Monday.AddDays(7), 0m, 400m),
                Week(Monday.AddDays(14), 50m, 50m),
            },
            Monday);

        Assert.Equal(3, layout.Bars.Count);
        Assert.Equal(Monday.AddDays(14), layout.Bars[2].WeekStart);
        Assert.True(layout.Bars[0].X < layout.Bars[1].X);
        Assert.True(layout.Bars[1].X < layout.Bars[2].X);
        Assert.Equal(layout.Bars[0].Width, layout.Bars[2].Width, 6);
    }

    [Fact]
    public void Today_is_marked_where_it_falls_inside_its_own_week()
    {
        // Wednesday: two days into a seven-day slot, so two sevenths across it.
        // Marking the week boundary instead would put "now" up to six days out.
        var wednesday = Monday.AddDays(2);
        var layout = TimelineLayout.Build(new[] { Week(Monday, 100m, 100m) }, wednesday);
        var bar = layout.Bars[0];

        Assert.NotNull(layout.NowX);
        Assert.Equal(bar.X + (bar.Width * 2 / 7), layout.NowX!.Value, 6);
    }

    [Fact]
    public void A_today_outside_the_plotted_weeks_is_not_marked_at_the_edge()
    {
        // Reachable from the Overview: an account whose bills are all historic.
        // Clamping the marker to the last bar would assert that today is that
        // week, which is worse than not drawing it.
        var layout = TimelineLayout.Build(
            new[] { Week(Monday, 100m, 0m) },
            Monday.AddDays(70));

        Assert.Null(layout.NowX);
    }

    [Fact]
    public void An_empty_week_still_gets_its_slot()
    {
        // WeekBuckets gap-fills, so quiet weeks arrive as zeroes. Dropping them
        // here would make the axis lie about how much time it covers.
        var layout = TimelineLayout.Build(
            new[] { Week(Monday, 100m, 0m), Week(Monday.AddDays(7), 0m, 0m, bills: 0) },
            Monday);

        Assert.Equal(2, layout.Bars.Count);
        Assert.Equal(0d, layout.Bars[1].PaidHeight);
        Assert.Equal(0d, layout.Bars[1].UnpaidHeight);
    }

    [Fact]
    public void No_weeks_at_all_draws_nothing_rather_than_dividing_by_zero()
    {
        var layout = TimelineLayout.Build(Array.Empty<WeekTotals>(), Monday);

        Assert.Empty(layout.Bars);
        Assert.Null(layout.NowX);
        Assert.Equal(0m, layout.AxisMax);
    }

    [Theory]
    [InlineData(1_750, 2_000)]
    [InlineData(1_000, 1_000)]
    [InlineData(30, 50)]
    [InlineData(6, 10)]
    [InlineData(0, 0)]
    public void The_axis_rounds_up_to_a_number_a_person_would_write(decimal max, decimal expected)
    {
        Assert.Equal(expected, TimelineLayout.NiceAxisMax(max));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter TimelineLayoutTests`
Expected: FAIL — `The name 'TimelineLayout' does not exist in the current context`.

- [ ] **Step 3: Write the layout**

Create `BillsMinimalApi.Contracts/TimelineLayout.cs`:

```csharp
using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>One week's stacked bar, in SVG user units.</summary>
public readonly record struct TimelineBar(
    DateTime WeekStart,
    double X,
    double Width,
    double PaidY,
    double PaidHeight,
    double UnpaidY,
    double UnpaidHeight,
    decimal Paid,
    decimal Unpaid);

/// <summary>A month label on the axis, at the week that starts it.</summary>
public readonly record struct TimelineTick(double X, string Label);

/// <summary>
/// The whole weekly timeline worked out in advance, so the component is markup
/// and nothing else.
/// <para>
/// The plot is authored in fixed user units and scaled by the browser, matching
/// how the app's other charts are drawn: the numbers here are resolution
/// independent, and nothing has to measure the viewport — which would need JS
/// interop, which does not exist during a prerendered first render.
/// </para>
/// </summary>
public sealed record TimelineLayout(
    List<TimelineBar> Bars,
    List<TimelineTick> Ticks,
    double? NowX,
    decimal AxisMax)
{
    public const double PlotLeft = 8;
    public const double PlotRight = 1192;
    public const double PlotTop = 12;
    public const double Baseline = 168;

    private const double PlotHeight = Baseline - PlotTop;

    /// <summary>Share of each week's slot the bar itself occupies; the rest is
    /// the gap to its neighbour.</summary>
    private const double BarFill = 0.7;

    public static TimelineLayout Build(IReadOnlyList<WeekTotals> weeks, DateTime today)
    {
        if (weeks.Count == 0)
        {
            return new TimelineLayout(new(), new(), null, 0m);
        }

        var axisMax = NiceAxisMax(weeks.Max(w => w.Total));

        var slot = (PlotRight - PlotLeft) / weeks.Count;
        var width = slot * BarFill;

        var bars = new List<TimelineBar>(weeks.Count);
        var ticks = new List<TimelineTick>();
        var lastMonth = 0;

        for (var i = 0; i < weeks.Count; i++)
        {
            var week = weeks[i];
            var x = PlotLeft + (slot * i) + ((slot - width) / 2);

            var paidHeight = Scale(week.Paid, axisMax);
            var unpaidHeight = Scale(week.Unpaid, axisMax);
            var paidY = Baseline - paidHeight;

            bars.Add(new TimelineBar(
                WeekStart: week.WeekStart,
                X: x,
                Width: width,
                PaidY: paidY,
                PaidHeight: paidHeight,
                // Unpaid rides on top of paid: what is settled is the part of
                // the bar that will not move again.
                UnpaidY: paidY - unpaidHeight,
                UnpaidHeight: unpaidHeight,
                Paid: week.Paid,
                Unpaid: week.Unpaid));

            // One label per month rather than per week — 260 week labels is a
            // grey smear, and the reader is orienting by month anyway.
            if (week.WeekStart.Month != lastMonth)
            {
                lastMonth = week.WeekStart.Month;
                ticks.Add(new TimelineTick(
                    x + (width / 2),
                    week.WeekStart.ToString("MMM", CultureInfo.CurrentCulture)));
            }
        }

        return new TimelineLayout(bars, ticks, NowX(bars, width, today), axisMax);
    }

    private static double Scale(decimal amount, decimal axisMax) =>
        axisMax == 0 ? 0 : (double)(amount / axisMax) * PlotHeight;

    /// <summary>
    /// Where "today" sits along the axis, or null when it is not on the plot at
    /// all — an account of nothing but historic bills has no now to mark, and a
    /// marker pinned to the last bar would claim otherwise.
    /// </summary>
    private static double? NowX(List<TimelineBar> bars, double width, DateTime today)
    {
        var thisWeek = WeekBuckets.StartOfWeek(today);
        var index = bars.FindIndex(b => b.WeekStart == thisWeek);

        if (index < 0)
        {
            return null;
        }

        // Proportional within the week, not on its boundary: a Friday marker on
        // Monday's line is up to four days of error on a chart about timing.
        var dayOfWeek = (today.Date - thisWeek).TotalDays;

        return bars[index].X + (width * dayOfWeek / 7);
    }

    /// <summary>
    /// Rounds up to 1, 2 or 5 times a power of ten — the ladder chart libraries
    /// use, so the axis reads as money at any scale.
    /// </summary>
    public static decimal NiceAxisMax(decimal max)
    {
        if (max <= 0)
        {
            return 0;
        }

        var magnitude = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)max)));
        var normalised = max / magnitude;

        var rounded = normalised switch
        {
            <= 1m => 1m,
            <= 2m => 2m,
            <= 5m => 5m,
            _ => 10m,
        };

        return rounded * magnitude;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter TimelineLayoutTests`
Expected: PASS, 12 tests (7 facts + 5 theory cases).

- [ ] **Step 5: Let the service settle one bill**

Append to `bills-frontend/BillsFrontEndBlazor/Services/BillService.cs`, after `DeleteBillAsync` (line 221):

```csharp
        /// <summary>
        /// Marks one bill paid given only its id.
        /// <para>
        /// Two round trips, and deliberately so. The Overview's late list is built
        /// from <see cref="SummaryBill"/>, which carries no concurrency token —
        /// that omission is the point of the type, since a report should not be
        /// able to write. Fetching the real bill is how this gets a token, and it
        /// means a bill someone else changed in the meantime comes back as a 409
        /// rather than being silently overwritten.
        /// </para>
        /// <para>
        /// The Bills page does not use this. It already holds every row with its
        /// token, so it writes them directly.
        /// </para>
        /// </summary>
        public async Task<BillWriteResult> MarkPaidAsync(long id, CancellationToken ct = default)
        {
            await AuthorizeAsync();

            Bill? bill;

            try
            {
                bill = await _http.GetFromJsonAsync<Bill>($"{Route}/{id}", ct);
            }
            catch (HttpRequestException ex)
            {
                // Same reason SendAsync catches it: an unhandled exception here
                // tears down the Blazor circuit and the page goes blank.
                return new BillWriteResult(false, ex.StatusCode);
            }

            if (bill is null)
            {
                return new BillWriteResult(false, HttpStatusCode.NotFound);
            }

            bill.Paid = true;

            return await UpdateBillAsync(bill, ct);
        }
```

- [ ] **Step 6: Write the timeline component**

Create `bills-frontend/BillsFrontEndBlazor/Shared/CashFlowTimeline.razor`:

```razor
@using System.Globalization
@using BillsMinimalApi.Contracts

@* Idea 2: the six-month history bar chart becomes the whole book, by week.
   Inline SVG rendered from C# — no interop, nothing fetched. *@
<section class="timeline">

    <div class="head">
        <div>
            <h2 class="title">Cash-flow timeline</h2>
            <p class="subtitle">Every bill on the books, by the week it falls due.</p>
        </div>

        <ul class="legend">
            <li><span class="swatch unpaid"></span>unpaid</li>
            <li><span class="swatch paid"></span>paid</li>
        </ul>
    </div>

    @if (_layout.Bars.Count == 0)
    {
        <p class="empty">No bills to plot yet.</p>
    }
    else
    {
        <svg class="plot" viewBox="0 0 1200 200" preserveAspectRatio="none"
             role="img" aria-label="Amount due each week, paid and unpaid">

            @foreach (var bar in _layout.Bars)
            {
                @* Wrapped in a <g> because a bare <text> at the top of a razor
                   loop is parsed as razor's own <text> escape tag. *@
                <g>
                    <rect class="paid" x="@N(bar.X)" y="@N(bar.PaidY)"
                          width="@N(bar.Width)" height="@N(bar.PaidHeight)">
                        <title>@Tooltip(bar)</title>
                    </rect>
                    <rect class="unpaid" x="@N(bar.X)" y="@N(bar.UnpaidY)"
                          width="@N(bar.Width)" height="@N(bar.UnpaidHeight)">
                        <title>@Tooltip(bar)</title>
                    </rect>
                </g>
            }

            <line class="baseline" x1="@N(TimelineLayout.PlotLeft)" x2="@N(TimelineLayout.PlotRight)"
                  y1="@N(TimelineLayout.Baseline)" y2="@N(TimelineLayout.Baseline)" />

            @if (_layout.NowX is { } nowX)
            {
                <g>
                    <line class="now" x1="@N(nowX)" x2="@N(nowX)"
                          y1="@N(TimelineLayout.PlotTop)" y2="@N(TimelineLayout.Baseline + 6)" />
                    <text class="now-label" x="@N(nowX)" y="@N(TimelineLayout.PlotTop - 2)"
                          text-anchor="middle">now</text>
                </g>
            }

            @foreach (var tick in _layout.Ticks)
            {
                <g>
                    <text class="tick" x="@N(tick.X)" y="188" text-anchor="middle">@tick.Label</text>
                </g>
            }

        </svg>
    }

</section>

@code {
    [Parameter, EditorRequired]
    public List<WeekTotals> Weeks { get; set; } = new();

    /// <summary>The server's date, from <c>BillSummary.AsOf</c> — never this
    /// machine's, or the marker could disagree with the bars beside it.</summary>
    [Parameter, EditorRequired]
    public DateTime Today { get; set; }

    private TimelineLayout _layout = TimelineLayout.Build(Array.Empty<WeekTotals>(), DateTime.Today);

    protected override void OnParametersSet() => _layout = TimelineLayout.Build(Weeks, Today);

    private static string Tooltip(TimelineBar bar) =>
        $"Week of {bar.WeekStart:MMM d}: {bar.Unpaid:C} unpaid, {bar.Paid:C} paid";

    // Invariant: these land in SVG coordinate attributes, which are not
    // culture-aware and would read "12,5" as two values.
    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
```

Create `bills-frontend/BillsFrontEndBlazor/Shared/CashFlowTimeline.razor.css`:

```css
.timeline {
    background: var(--surface);
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius-lg);
    padding: 1.25rem 1.5rem;
}

.head {
    align-items: flex-start;
    display: flex;
    justify-content: space-between;
}

.title {
    color: var(--text);
    font-size: 1rem;
    font-weight: 600;
    margin: 0;
}

.subtitle,
.empty {
    color: var(--muted);
    font-size: .85rem;
    margin: .15rem 0 0;
}

.legend {
    color: var(--muted);
    display: flex;
    font-size: .78rem;
    gap: 1rem;
    list-style: none;
    margin: 0;
    padding: 0;
}

.legend li {
    align-items: center;
    display: flex;
    gap: .35rem;
}

.swatch {
    border-radius: 2px;
    height: 9px;
    width: 9px;
}

.swatch.unpaid { background: var(--accent); }
.swatch.paid { background: var(--ok); }

.plot {
    display: block;
    height: 200px;
    margin-top: 1rem;
    width: 100%;
}

/* ::deep, because these elements are inside a loop this component renders but
   Blazor's scoped-CSS attribute is only stamped on the outermost markup. */
::deep rect.unpaid { fill: var(--accent); }
::deep rect.paid { fill: var(--ok); }
::deep line.baseline { stroke: var(--border); stroke-width: 1; }

::deep line.now {
    stroke: var(--late);
    stroke-dasharray: 3 3;
    stroke-width: 1.5;
}

::deep text.now-label {
    fill: var(--late);
    font-size: 11px;
}

::deep text.tick {
    fill: var(--faint);
    font-size: 11px;
}
```

- [ ] **Step 7: Write the late list**

Create `bills-frontend/BillsFrontEndBlazor/Shared/LateBillsList.razor`:

```razor
@using BillsMinimalApi.Contracts

@* Idea 3: the overdue list becomes triageable where it sits. The old flow was
   read here, navigate to Bills, find the row, open a modal, tick a box, save. *@
<section class="late">

    <div class="head">
        <div>
            <h2 class="title">Late — oldest first</h2>
            <p class="subtitle">The only thing on this page that needs doing today.</p>
        </div>
        <p class="total">@OverdueCount @(OverdueCount == 1 ? "bill" : "bills") · @OverdueAmount.ToString("C")</p>
    </div>

    @if (Bills.Count == 0)
    {
        <p class="empty">Nothing is late.</p>
    }
    else
    {
        <ul class="rows">
            @foreach (var bill in Bills)
            {
                <li @key="bill.Id">
                    <span class="payee">@bill.PayeeName</span>
                    <span class="days">@bill.DaysLate @(bill.DaysLate == 1 ? "day" : "days") late</span>
                    <span class="due">@bill.DueDate.ToString("MMM d")</span>
                    <span class="amount">@bill.PaymentDue.ToString("C")</span>

                    <button type="button" class="settle"
                            disabled="@(BusyId is not null)"
                            @onclick="@(() => OnMarkPaid.InvokeAsync(bill))">
                        @if (BusyId == bill.Id)
                        {
                            <Icon Name="circle-notch" Size="16" />
                        }
                        else
                        {
                            <Icon Name="check" Size="16" />
                        }
                        Mark paid
                    </button>
                </li>
            }
        </ul>

        @if (OverdueCount > Bills.Count)
        {
            @* The server caps the list. Saying so is the difference between a
               shortlist and a list that quietly stops. *@
            <p class="capped">Showing the first @Bills.Count of @OverdueCount late bills.</p>
        }
    }

</section>

@code {
    [Parameter, EditorRequired]
    public List<SummaryBill> Bills { get; set; } = new();

    /// <summary>Every late bill, not just the ones listed — the header counts the
    /// whole problem even when the list is capped.</summary>
    [Parameter, EditorRequired]
    public int OverdueCount { get; set; }

    [Parameter, EditorRequired]
    public decimal OverdueAmount { get; set; }

    /// <summary>The bill currently being written, if any. Every button disables
    /// while one is in flight: two overlapping writes would both reload the page
    /// underneath each other.</summary>
    [Parameter]
    public long? BusyId { get; set; }

    [Parameter]
    public EventCallback<SummaryBill> OnMarkPaid { get; set; }
}
```

Create `bills-frontend/BillsFrontEndBlazor/Shared/LateBillsList.razor.css`:

```css
.late {
    background: var(--surface);
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius-lg);
    padding: 1.25rem 1.5rem;
}

.head {
    align-items: flex-start;
    display: flex;
    justify-content: space-between;
}

.title {
    color: var(--text);
    font-size: 1rem;
    font-weight: 600;
    margin: 0;
}

.subtitle,
.empty,
.capped {
    color: var(--muted);
    font-size: .85rem;
    margin: .15rem 0 0;
}

.total {
    color: var(--late);
    font-variant-numeric: tabular-nums;
    margin: 0;
}

.rows {
    list-style: none;
    margin: 1rem 0 0;
    padding: 0;
}

/* A grid rather than a table: five fixed columns, so the payee takes the slack
   and the numbers stay in line down the page. */
.rows li {
    align-items: center;
    border-top: var(--border-width) solid var(--border);
    display: grid;
    gap: 1rem;
    grid-template-columns: 1fr auto 5.5rem 6.5rem auto;
    padding: .55rem 0;
}

.rows li:first-child {
    border-top: 0;
}

.payee { color: var(--text); }

.days { color: var(--late); font-size: .8rem; }

.due,
.amount {
    color: var(--muted);
    font-variant-numeric: tabular-nums;
    text-align: right;
}

.amount { color: var(--text); }

.settle {
    align-items: center;
    background: none;
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius);
    color: var(--muted);
    cursor: pointer;
    display: inline-flex;
    font-size: .8rem;
    gap: .35rem;
    padding: .3rem .6rem;
}

.settle:hover:not(:disabled) {
    border-color: var(--accent);
    color: var(--accent-text);
}

.settle:disabled {
    cursor: default;
    opacity: .5;
}

.capped {
    margin-top: .85rem;
}
```

- [ ] **Step 8: Rewrite the Overview markup**

Replace the entire contents of `bills-frontend/BillsFrontEndBlazor/Pages/Index.razor`:

```razor
@page "/"
@attribute [Authorize]

<PageTitle>Overview</PageTitle>

<div class="overview">

    <header class="page-head">
        <div>
            <h1>Overview</h1>
            @* The server's date, not this machine's — every figure below was
               computed against it. *@
            <p class="as-of">as of @Summary.AsOf.ToString("MMM d, yyyy")</p>
        </div>

        <button class="refresh" @onclick="LoadStatsAsync" disabled="@_isLoading">
            <Icon Name="arrows-clockwise" Size="16" Class="me-1" />
            Refresh
        </button>
    </header>

    @if (_loadFailed)
    {
        <div class="alert alert-danger d-flex align-items-center gap-3" role="alert">
            <Icon Name="warning-octagon" Size="22" />
            <div class="flex-grow-1">Could not reach the API.</div>
            <button class="btn btn-sm btn-outline-danger" @onclick="LoadStatsAsync">
                <Icon Name="arrows-clockwise" Size="16" Class="me-1" /> Retry
            </button>
        </div>
    }

    <ObligationHeadline Summary="@Summary" />

    <AgingStrip Buckets="@Summary.Aging" />

    <CashFlowTimeline Weeks="@Summary.Weeks" Today="@_today" />

    <LateBillsList Bills="@Summary.Late"
                   OverdueCount="@Summary.OverdueCount"
                   OverdueAmount="@Summary.OverdueAmount"
                   BusyId="@_busyId"
                   OnMarkPaid="@MarkPaidAsync" />

</div>
```

Create `bills-frontend/BillsFrontEndBlazor/Pages/Index.razor.css`:

```css
.overview {
    display: flex;
    flex-direction: column;
    gap: 1.25rem;
    margin: 0 auto;
    max-width: 1200px;
    padding: 1.5rem;
}

.page-head {
    align-items: flex-start;
    display: flex;
    justify-content: space-between;
}

.page-head h1 {
    color: var(--text);
    font-size: 1.4rem;
    font-weight: 600;
    margin: 0;
}

.as-of {
    color: var(--faint);
    font-size: .82rem;
    margin: .1rem 0 0;
}

.refresh {
    align-items: center;
    background: none;
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius);
    color: var(--muted);
    cursor: pointer;
    display: inline-flex;
    padding: .4rem .8rem;
}

.refresh:hover:not(:disabled) {
    color: var(--text);
}

.refresh:disabled {
    cursor: default;
    opacity: .5;
}
```

- [ ] **Step 9: Rewrite the Overview code-behind**

Replace the entire contents of `bills-frontend/BillsFrontEndBlazor/Pages/Index.razor.cs`:

```csharp
using BillsFrontEndBlazor.Services;
using BillsMinimalApi.Contracts;
using Microsoft.AspNetCore.Components;

namespace BillsFrontEndBlazor.Pages
{
    /// <summary>
    /// The Overview. One <see cref="BillSummary"/> for the whole book, rendered
    /// as four sections: what is owed, how late it is, when it falls, and what
    /// needs settling today.
    /// <para>
    /// All the chart geometry that used to live here has moved into
    /// <see cref="TimelineLayout"/> and <see cref="StackedStrip"/>, where it can
    /// be tested. What is left is loading, and one write.
    /// </para>
    /// </summary>
    public partial class Index : IDisposable
    {
        [Inject]
        public BillService BillService { get; set; } = default!;

        [Inject]
        public BillEventService BillEventService { get; set; } = default!;

        [Inject]
        public ToastService Toasts { get; set; } = default!;

        /// <summary>Stands in until the first response lands, so the markup can
        /// read a summary without a null check per section.</summary>
        private static readonly BillSummary NoData = new();

        private BillSummary? _summary;

        private bool _isLoading = true;
        private bool _loadFailed;

        /// <summary>The date the figures were computed against — the server's,
        /// from <see cref="BillSummary.AsOf"/>, so the timeline's "now" marker
        /// agrees with the bars beside it.</summary>
        private DateTime _today = DateTime.Today;

        /// <summary>Which late bill is being settled, if any.</summary>
        private long? _busyId;

        private BillSummary Summary => _summary ?? NoData;

        protected override async Task OnInitializedAsync()
        {
            BillEventService.OnBillsChanged += RefreshDashboard;
            await LoadStatsAsync();
        }

        public void Dispose() => BillEventService.OnBillsChanged -= RefreshDashboard;

        private void RefreshDashboard()
        {
            // Not `async void`: that form throws on a pooled thread with nobody
            // to catch it if the API is down. InvokeAsync also puts the work back
            // on the circuit's synchronization context, which StateHasChanged
            // requires.
            _ = InvokeAsync(LoadStatsAsync);
        }

        private async Task LoadStatsAsync()
        {
            _isLoading = true;
            _loadFailed = false;
            StateHasChanged();

            try
            {
                // No window: the Overview is about everything on record, so it
                // asks for the unbounded summary.
                var summary = await BillService.GetSummaryAsync(from: null, to: null);

                _summary = summary;
                _today = summary.AsOf;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _summary = NoData;

                // Back to this machine's date: NoData.AsOf is default(DateTime),
                // and a "now" marker in year 1 is not a marker.
                _today = DateTime.Today;
                _loadFailed = true;
                Toasts.ShowError("Could not load the overview. Is the API running?");
            }
            finally
            {
                _isLoading = false;
                StateHasChanged();
            }
        }

        private async Task MarkPaidAsync(SummaryBill bill)
        {
            if (_busyId is not null)
            {
                return;
            }

            _busyId = bill.Id;
            StateHasChanged();

            var result = await BillService.MarkPaidAsync(bill.Id);

            _busyId = null;

            if (result.Success)
            {
                Toasts.ShowSuccess($"{bill.PayeeName} marked paid.");

                // Every other open page recomputes too — and this page is
                // subscribed, so this is also what reloads it.
                BillEventService.NotifyBillsChanged();
                return;
            }

            Toasts.ShowError(result.ToMessage("update"));

            // A 409 or a 404 both mean this page is looking at stale data, so the
            // honest response is to go and get the current answer.
            await LoadStatsAsync();
        }
    }
}
```

- [ ] **Step 10: Run the whole suite**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln`
Expected: PASS. This also builds the Blazor project, so a missing component or a renamed parameter fails here.

- [ ] **Step 11: Verify it by eye**

Run the app and check the Overview:
1. The sentence, the strip, the timeline and the late list all render with real seed data.
2. The timeline's "now" line falls inside the current week, not at its edge.
3. "Mark paid" on a late row removes it from the list and drops the headline total by that amount.
4. All four palette×mode combinations leave every bar, band and marker readable.

- [ ] **Step 12: Commit**

```bash
git add -A bills-frontend/BillsFrontEndBlazor BillsMinimalApi.Contracts/TimelineLayout.cs tests/BillsMinimalApi.UnitTests/TimelineLayoutTests.cs
git commit -m "Rebuild the Overview around what is owed and what is late"
```

---

### Task 9: Due-window groups replace pagination on Bills

Idea 5. The table stops being a pager over a window and becomes the whole book, partitioned into five sections that each carry their own count and sum.

Two capabilities go with the pager, deliberately:
- **Column sorting and the rows-per-page select.** The groups impose their own order (due date ascending), which is the point of idea 5 — a book sorted by amount has no due windows in it. The spec's Bills composition lists chips, search, a count line, groups, checkboxes, inline edit and quick-add, and no sort control.
- **Page navigation.** Replaced by the 500-row cap and its notice.

**One thing this page does not get:** the "as of &lt;date&gt;" line under the heading. Overview and Reports both carry it because both read a `BillSummary`, and `AsOf` is the server's own answer to "what day is it" — so neither screen has to trust the browser's clock. Bills reads rows and a count; there is no summary in the response to take a date from, and fetching one purely to print a date would add a round trip the spec's data-flow section exists to avoid. It gets the lede instead. The date the groups are actually cut against is `DateTime.Today` on the Blazor Server host, which is the same machine serving the API — documented at the field itself in Step 7.

**Files:**
- Create: `BillsMinimalApi.Contracts/DueWindows.cs`
- Create: `tests/BillsMinimalApi.UnitTests/DueWindowsTests.cs`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/BillGroup.razor`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/BillGroup.razor.css`
- Modify: `bills-frontend/BillsFrontEndBlazor/Services/BillService.cs` (add `BillBook` beside `BillWriteResult` at line 38, and `GetBookAsync` after `GetAllInRangeAsync` at line 195)
- Modify: `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor` (replace lines 6–286 — the `<div class="container py-4">` block; **leave both modal blocks below it exactly as they are**)
- Rewrite: `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor.cs`
- Create: `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor.css`

**Interfaces:**
- Consumes: `WeekBuckets.StartOfWeek` (Task 3), `<Icon>` (Task 6).
- Produces:
  - `enum DueWindow { Late, ThisWeek, ThisMonth, Later, Paid }`
  - `DueWindows.Classify(bool paid, DateTime? dueDate, DateTime today) -> DueWindow`
  - `DueWindows.Title(DueWindow window) -> string`, `DueWindows.Order -> IReadOnlyList<DueWindow>`
  - `DueWindows.EndOfWeek(DateTime today) -> DateTime`, `DueWindows.EndOfMonth(DateTime today) -> DateTime`
  - `BillBook(List<Bill> Bills, int TotalCount)` with `IsCapped` and `BillBook.Empty`
  - `BillService.GetBookAsync(BillQuery seed, int cap, CancellationToken ct = default) -> Task<BillBook>`
  - `<BillGroup Title Tone Bills Today BusyIds OnTogglePaid OnEdit OnDelete />` — Task 10 adds selection parameters to this component, Task 11 replaces its date and amount cells.

- [ ] **Step 1: Write the failing classification tests**

Create `tests/BillsMinimalApi.UnitTests/DueWindowsTests.cs`:

```csharp
using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// Which section of the Bills page a bill lands in. Five predicates that have to
/// be mutually exclusive and cover everything: a bill in two groups is counted
/// twice in two sums, and a bill in none silently disappears from a page that
/// claims to be the whole book.
/// </summary>
public sealed class DueWindowsTests
{
    // Wednesday. Its week runs Mon 17th to Sun 23rd, and its month ends Mon 31st.
    private static readonly DateTime Wednesday = new(2026, 8, 19);

    private static DateTime On(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void A_paid_bill_is_paid_however_late_it_was()
    {
        // Paid is checked before anything about the date, so settling a bill
        // moves it out of Late rather than leaving it in two groups at once.
        Assert.Equal(
            DueWindow.Paid,
            DueWindows.Classify(paid: true, On(2025, 1, 1), Wednesday));
    }

    [Fact]
    public void Yesterday_and_unpaid_is_late()
    {
        Assert.Equal(
            DueWindow.Late,
            DueWindows.Classify(paid: false, On(2026, 8, 18), Wednesday));
    }

    [Fact]
    public void Today_is_not_late_yet()
    {
        // A bill due today has all day to be paid. Calling it late would put
        // eight bills in the Late group that the Overview's sentence did not
        // count, because the API's own OverdueCount uses the same rule.
        Assert.Equal(
            DueWindow.ThisWeek,
            DueWindows.Classify(paid: false, Wednesday, Wednesday));
    }

    [Fact]
    public void The_week_runs_to_its_sunday_inclusive()
    {
        Assert.Equal(
            DueWindow.ThisWeek,
            DueWindows.Classify(paid: false, On(2026, 8, 23), Wednesday));

        Assert.Equal(
            DueWindow.ThisMonth,
            DueWindows.Classify(paid: false, On(2026, 8, 24), Wednesday));
    }

    [Fact]
    public void The_month_runs_to_its_last_day_inclusive()
    {
        Assert.Equal(
            DueWindow.ThisMonth,
            DueWindows.Classify(paid: false, On(2026, 8, 31), Wednesday));

        Assert.Equal(
            DueWindow.Later,
            DueWindows.Classify(paid: false, On(2026, 9, 1), Wednesday));
    }

    [Fact]
    public void A_week_that_runs_past_the_end_of_the_month_still_wins()
    {
        // Monday the 31st: this week ends Sun 6 September, this month ends
        // today. "Due this week" is checked first, so a bill due on the 3rd is
        // this week's problem rather than being pushed out to Later.
        var monthEnd = On(2026, 8, 31);

        Assert.Equal(
            DueWindow.ThisWeek,
            DueWindows.Classify(paid: false, On(2026, 9, 3), monthEnd));

        Assert.Equal(
            DueWindow.Later,
            DueWindows.Classify(paid: false, On(2026, 9, 7), monthEnd));
    }

    [Fact]
    public void On_a_sunday_the_week_ends_today_rather_than_seven_days_out()
    {
        // The prototype's `today + (7 - getDay())` gives next Sunday when today
        // is a Sunday, which would drag a whole extra week into "Due this week".
        // Weeks here start on Monday, the same as the timeline's buckets, so a
        // Sunday is the end of its own week.
        var sunday = On(2026, 8, 30);

        Assert.Equal(
            DueWindow.ThisWeek,
            DueWindows.Classify(paid: false, sunday, sunday));

        Assert.Equal(
            DueWindow.ThisMonth,
            DueWindows.Classify(paid: false, On(2026, 8, 31), sunday));
    }

    [Fact]
    public void A_bill_with_no_due_date_falls_to_the_end_rather_than_vanishing()
    {
        // The API always sends one, but the client's Bill model makes DueDate
        // nullable so the create form can fail validation rather than crash.
        // Later is where an unknown date is least disruptive — it is not late,
        // and it is not being claimed as due this week.
        Assert.Equal(
            DueWindow.Later,
            DueWindows.Classify(paid: false, null, Wednesday));
    }

    [Fact]
    public void A_time_of_day_does_not_move_a_bill_between_groups()
    {
        // Due dates arrive as midnight UTC, but a bill edited through the form
        // can carry a local time. Comparing dates rather than instants is what
        // stops "due today at 09:00" reading as late by lunchtime.
        Assert.Equal(
            DueWindow.ThisWeek,
            DueWindows.Classify(paid: false, Wednesday.AddHours(9), Wednesday.AddHours(17)));
    }

    [Fact]
    public void Every_window_has_a_title_and_they_come_in_reading_order()
    {
        Assert.Equal(
            new[] { "Late", "Due this week", "Due this month", "Later", "Paid" },
            DueWindows.Order.Select(DueWindows.Title));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter DueWindowsTests`
Expected: FAIL — `The name 'DueWindows' does not exist in the current context`.

- [ ] **Step 3: Write the classifier**

Create `BillsMinimalApi.Contracts/DueWindows.cs`:

```csharp
namespace BillsMinimalApi.Contracts;

/// <summary>Which section of the Bills page a bill belongs to.</summary>
public enum DueWindow
{
    Late,
    ThisWeek,
    ThisMonth,
    Later,
    Paid,
}

/// <summary>
/// The grammar behind the Bills page's five sections.
/// <para>
/// In the contracts project rather than in the page, because the page cannot be
/// unit tested — and because these five predicates have to stay mutually
/// exclusive and exhaustive, which is a property worth asserting rather than
/// eyeballing.
/// </para>
/// </summary>
public static class DueWindows
{
    /// <summary>Reading order: the thing that needs doing first is first.</summary>
    public static IReadOnlyList<DueWindow> Order { get; } = new[]
    {
        DueWindow.Late,
        DueWindow.ThisWeek,
        DueWindow.ThisMonth,
        DueWindow.Later,
        DueWindow.Paid,
    };

    public static string Title(DueWindow window) => window switch
    {
        DueWindow.Late => "Late",
        DueWindow.ThisWeek => "Due this week",
        DueWindow.ThisMonth => "Due this month",
        DueWindow.Later => "Later",
        _ => "Paid",
    };

    /// <summary>
    /// The Sunday that closes the week <paramref name="today"/> falls in.
    /// <para>
    /// Built from <see cref="WeekBuckets.StartOfWeek"/> so the Bills page and the
    /// Overview's timeline agree about where a week begins. The design prototype
    /// computed this as <c>today + (7 - dayOfWeek)</c>, which returns next Sunday
    /// when today is a Sunday — a whole extra week of bills described as due this
    /// one.
    /// </para>
    /// </summary>
    public static DateTime EndOfWeek(DateTime today) =>
        WeekBuckets.StartOfWeek(today).AddDays(6);

    public static DateTime EndOfMonth(DateTime today) =>
        new(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

    /// <summary>
    /// Which section a bill belongs to. Ordered so the first match wins: paid
    /// beats every date, and late beats every deadline.
    /// </summary>
    /// <param name="dueDate">
    /// Null lands in <see cref="DueWindow.Later"/>. The API always sends a due
    /// date; the client's model allows null so its create form can fail
    /// validation rather than throw.
    /// </param>
    public static DueWindow Classify(bool paid, DateTime? dueDate, DateTime today)
    {
        if (paid)
        {
            return DueWindow.Paid;
        }

        if (dueDate is not { } due)
        {
            return DueWindow.Later;
        }

        // Dates, not instants. Due dates are stored at midnight UTC and a bill
        // edited through the form can carry a local time; comparing the whole
        // value would make "due today" read as late by mid-morning.
        var day = due.Date;
        var now = today.Date;

        if (day < now)
        {
            return DueWindow.Late;
        }

        if (day <= EndOfWeek(now))
        {
            return DueWindow.ThisWeek;
        }

        // Checked after the week deliberately: in the last days of a month the
        // week runs past the month end, and a bill three days out is this week's
        // problem rather than one to defer to Later.
        return day <= EndOfMonth(now) ? DueWindow.ThisMonth : DueWindow.Later;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter DueWindowsTests`
Expected: PASS, 10 tests.

- [ ] **Step 5: Teach the service to walk the whole book**

Add to `bills-frontend/BillsFrontEndBlazor/Services/BillService.cs`, immediately after the `BillWriteResult` record (line 38) and before `public class BillService`:

```csharp
    /// <summary>
    /// Every bill matching a query — up to a cap — together with how many there
    /// really are.
    /// <para>
    /// <see cref="TotalCount"/> is the server's answer, not
    /// <c>Bills.Count</c>. Keeping both is what lets the page say "showing the
    /// first 500 of 1,240" instead of quietly presenting 500 as the whole book.
    /// </para>
    /// </summary>
    public sealed record BillBook(List<Bill> Bills, int TotalCount)
    {
        public static BillBook Empty { get; } = new(new List<Bill>(), 0);

        public bool IsCapped => Bills.Count < TotalCount;
    }
```

Add to the same file, after `GetAllInRangeAsync` (line 195):

```csharp
        /// <summary>
        /// Walks the paged endpoint until the query is exhausted or
        /// <paramref name="cap"/> rows are in hand.
        /// <para>
        /// The Bills page groups by due window, and a due window spans the whole
        /// book — so it can no longer ask for one page at a time. This is the
        /// same walk <see cref="GetAllInRangeAsync"/> does for CSV export, with
        /// two differences: it carries the page's own filter, search and sort
        /// rather than fetching everything, and it stops.
        /// </para>
        /// <para>
        /// The cap is a real bound, not a formality. Without it a 20,000-row
        /// account would render 20,000 rows into a Blazor Server circuit and send
        /// the whole diff over the wire.
        /// </para>
        /// </summary>
        /// <param name="seed">
        /// The query to repeat. Its <c>Page</c> and <c>PageSize</c> are ignored —
        /// the walk sets them.
        /// </param>
        public async Task<BillBook> GetBookAsync(
            BillQuery seed,
            int cap,
            CancellationToken ct = default)
        {
            var all = new List<Bill>();
            var page = 1;
            var total = 0;

            while (true)
            {
                var result = await GetBillsAsync(
                    seed with { Page = page, PageSize = BillQuery.MaxPageSize },
                    ct);

                // Every page carries it, and it is the same number each time; the
                // last one read is as good as the first.
                total = result.TotalCount;
                all.AddRange(result.Items);

                if (all.Count >= cap || !result.HasNext)
                {
                    break;
                }

                page = result.Page + 1;
            }

            // A page is 100 rows, so a cap of 500 lands exactly — but the cap is a
            // constant someone may change, and 501 rows under a cap of 450 would
            // otherwise be handed to the page as if it had asked for them.
            if (all.Count > cap)
            {
                all.RemoveRange(cap, all.Count - cap);
            }

            return new BillBook(all, total);
        }
```

- [ ] **Step 6: Write the group component**

Create `bills-frontend/BillsFrontEndBlazor/Shared/BillGroup.razor`:

```razor
@using BillsFrontEndBlazor.Models

@* Idea 5: one due-window section, with its own count and sum. The row markup
   lives here rather than in Bills.razor so all five sections stay one source of
   truth — the alternative was the same six cells written five times. *@
<section class="group">

    <header>
        <span class="dot" style="background: @Tone"></span>
        <h2 class="title">@Title</h2>
        <span class="count">@Bills.Count @(Bills.Count == 1 ? "bill" : "bills")</span>
        <span class="sum">@Total.ToString("C")</span>
    </header>

    <ul class="rows">
        @foreach (var bill in Bills)
        {
            <li @key="bill.Id" class="@(IsOverdue(bill, Today) ? "overdue" : null)">

                <span class="payee">@bill.PayeeName</span>

                <span class="due">
                    @DueDateText(bill)
                    @if (DueRelativeText(bill, Today) is { } relative)
                    {
                        <span class="relative">@relative</span>
                    }
                </span>

                @* ToString("C") renders "$1,234.56" only because Program.cs pins
                   en-US; the container default is the invariant culture, which
                   formats "C" as ¤. *@
                <span class="amount">@bill.PaymentDue.ToString("C")</span>

                @* A button rather than a badge with a click handler: this is the
                   one control on the row that changes data, so it has to be
                   reachable by keyboard and announce its state. aria-pressed is
                   written out rather than bound to the bool because Blazor drops
                   a false attribute entirely, and it needs the literal "false"
                   to read as an unpressed toggle. *@
                <button type="button"
                        class="status @StatusClass(bill, Today)"
                        title="@(bill.Paid ? "Mark as unpaid" : "Mark as paid")"
                        aria-pressed="@(bill.Paid ? "true" : "false")"
                        disabled="@BusyIds.Contains(bill.Id)"
                        @onclick="@(() => OnTogglePaid.InvokeAsync(bill))">
                    @if (BusyIds.Contains(bill.Id))
                    {
                        <span class="spinner-border spinner-border-sm" role="status"></span>
                    }
                    else
                    {
                        @StatusText(bill, Today)
                    }
                </button>

                <span class="actions">
                    <button type="button" class="icon-button" title="Edit"
                            aria-label="Edit @bill.PayeeName"
                            @onclick="@(() => OnEdit.InvokeAsync(bill))">
                        <Icon Name="pencil" Size="16" />
                    </button>

                    <button type="button" class="icon-button danger" title="Delete"
                            aria-label="Delete @bill.PayeeName"
                            @onclick="@(() => OnDelete.InvokeAsync(bill))">
                        <Icon Name="trash" Size="16" />
                    </button>
                </span>

            </li>
        }
    </ul>

</section>

@code {
    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    /// <summary>A CSS colour for the section's dot — a `var(--token)` from the
    /// caller, so this component never names a palette.</summary>
    [Parameter]
    public string Tone { get; set; } = "var(--text)";

    [Parameter, EditorRequired]
    public List<Bill> Bills { get; set; } = new();

    [Parameter, EditorRequired]
    public decimal Total { get; set; }

    [Parameter, EditorRequired]
    public DateTime Today { get; set; }

    /// <summary>Bills with a write in flight. A set rather than one flag so a
    /// slow row cannot freeze the whole section.</summary>
    [Parameter]
    public IReadOnlySet<long> BusyIds { get; set; } = new HashSet<long>();

    [Parameter]
    public EventCallback<Bill> OnTogglePaid { get; set; }

    [Parameter]
    public EventCallback<Bill> OnEdit { get; set; }

    [Parameter]
    public EventCallback<Bill> OnDelete { get; set; }

    /// <summary>
    /// Past its due date and still unpaid. A section can contain an overdue bill
    /// even when it is not the Late section: toggling a paid bill back to unpaid
    /// leaves it here until the reload lands.
    /// </summary>
    private static bool IsOverdue(Bill bill, DateTime today) =>
        !bill.Paid && bill.DueDate is { } due && due.Date < today.Date;

    /// <summary>Three states from two booleans: an unpaid bill that is not due
    /// yet is not a problem, so the alarm colour is saved for the ones that
    /// are.</summary>
    private static string StatusClass(Bill bill, DateTime today) => bill switch
    {
        { Paid: true } => "is-paid",
        _ when IsOverdue(bill, today) => "is-late",
        _ => "is-unpaid",
    };

    private static string StatusText(Bill bill, DateTime today) => bill switch
    {
        { Paid: true } => "Paid",
        _ when IsOverdue(bill, today) => "Overdue",
        _ => "Unpaid",
    };

    /// <summary>The date itself. Spelled out rather than "6/2/2026", which reads
    /// as either 6 February or June 2 depending on where you are from.</summary>
    private static string DueDateText(Bill bill) =>
        bill.DueDate?.ToString("MMM d, yyyy") ?? "—";

    /// <summary>How far off the due date is, in plain words. Only for unpaid
    /// bills — once something is paid, how late it was is history.</summary>
    private static string? DueRelativeText(Bill bill, DateTime today)
    {
        if (bill.Paid || bill.DueDate is not { } due)
        {
            return null;
        }

        var days = (due.Date - today.Date).Days;

        return days switch
        {
            0 => "due today",
            1 => "due tomorrow",
            < 0 and > -2 => "1 day late",
            < 0 => $"{-days} days late",
            <= 7 => $"in {days} days",
            _ => null,
        };
    }
}
```

Create `bills-frontend/BillsFrontEndBlazor/Shared/BillGroup.razor.css`:

```css
.group {
    background: var(--surface);
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius-lg);
    padding: 1rem 1.25rem;
}

header {
    align-items: baseline;
    display: flex;
    gap: .6rem;
}

.dot {
    align-self: center;
    border-radius: 50%;
    height: 8px;
    width: 8px;
}

.title {
    color: var(--text);
    font-size: .95rem;
    font-weight: 600;
    margin: 0;
}

.count {
    color: var(--muted);
    font-size: .82rem;
}

.sum {
    color: var(--text);
    font-variant-numeric: tabular-nums;
    margin-left: auto;
}

.rows {
    list-style: none;
    margin: .75rem 0 0;
    padding: 0;
}

/* Five fixed columns and one flexible one, so the payee takes the slack and
   every number below it stays in a line. Task 10 adds a checkbox column to the
   front of this template. */
.rows li {
    align-items: center;
    border-top: var(--border-width) solid var(--border);
    display: grid;
    gap: 1rem;
    grid-template-columns: 1fr 11rem 7rem 5.5rem auto;
    padding: .5rem 0;
}

.rows li:first-child {
    border-top: 0;
}

.payee {
    color: var(--text);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.due {
    color: var(--muted);
    font-size: .85rem;
}

.relative {
    color: var(--faint);
    display: block;
    font-size: .75rem;
}

.overdue .relative {
    color: var(--late);
}

.amount {
    color: var(--text);
    font-variant-numeric: tabular-nums;
    text-align: right;
}

.status {
    background: none;
    border: var(--border-width) solid var(--border);
    border-radius: 999px;
    cursor: pointer;
    font-size: .75rem;
    padding: .15rem .6rem;
    width: 100%;
}

/* Outline only, never a flood fill — the palette's accent rule, and it is what
   keeps five sections of these from reading as a colour chart. */
.status.is-paid { border-color: var(--ok); color: var(--ok); }
.status.is-late { border-color: var(--late); color: var(--late); }
.status.is-unpaid { color: var(--muted); }

.status:disabled {
    cursor: default;
    opacity: .5;
}

.actions {
    display: flex;
    gap: .35rem;
}

.icon-button {
    background: none;
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius);
    color: var(--muted);
    cursor: pointer;
    line-height: 1;
    padding: .3rem .45rem;
}

.icon-button:hover {
    border-color: var(--accent);
    color: var(--accent-text);
}

.icon-button.danger:hover {
    border-color: var(--late);
    color: var(--late);
}
```

- [ ] **Step 7: Rewrite the Bills page body**

In `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor`, replace lines 6–286 — the whole `<div class="container py-4">` block — with the markup below. **Leave the two modal blocks that follow it untouched;** Task 11 is what changes those.

```razor
<div class="bills">

    <header class="page-head">
        <div>
            <h1>Bills</h1>
            <p class="lede">Every bill on the books, grouped by when it falls due.</p>
        </div>

        <div class="head-actions">
            <button type="button" class="ghost" @onclick="LoadBillsAsync" disabled="@_isLoading">
                <Icon Name="arrows-clockwise" Size="16" Class="me-1" />
                Refresh
            </button>

            <button type="button" class="primary" @onclick="OpenCreateModal">
                <Icon Name="plus" Size="16" Class="me-1" />
                Add bill
            </button>
        </div>
    </header>

    @if (_loadFailed)
    {
        <div class="alert alert-danger d-flex align-items-center gap-3" role="alert">
            <Icon Name="warning-octagon" Size="22" />
            <div class="flex-grow-1">Could not reach the API.</div>
            <button class="btn btn-sm btn-outline-danger" @onclick="LoadBillsAsync">
                <Icon Name="arrows-clockwise" Size="16" Class="me-1" /> Retry
            </button>
        </div>
    }

    <div class="controls">

        <div class="chips" role="group" aria-label="Filter bills by status">
            @foreach (var option in FilterOrder)
            {
                <button type="button"
                        class="chip @(_filter == option ? "active" : null)"
                        @onclick="@(() => SetFilterAsync(option))">
                    @option
                    @* Only on Overdue, and only when there are some: a "0" badge
                       on every chip is noise, and the whole point of the count is
                       to pull attention when it is not zero. *@
                    @if (option == BillStatus.Overdue && OverdueCount > 0)
                    {
                        <span class="chip-badge">@OverdueCount</span>
                    }
                </button>
            }
        </div>

        <div class="search">
            <Icon Name="magnifying-glass" Size="16" Class="search-icon" />
            @* One-way plus @oninput rather than @bind: the search goes to the
               server, and two-way binding would send a request per keystroke.
               OnSearchInputAsync debounces instead. *@
            <input placeholder="Search by payee…"
                   aria-label="Search by payee"
                   value="@_searchText"
                   @oninput="OnSearchInputAsync" />

            @* Disabled when empty rather than hidden: a control that appears and
               disappears as you type moves the search box sideways. *@
            <button type="button" class="ghost" @onclick="ClearSearchAsync" disabled="@(!HasSearch)">
                Clear
            </button>
        </div>

        <p class="tally">@MatchCount of @BillCount bills · @LoadedTotal.ToString("C")</p>

    </div>

    @* Only when there is nothing to show yet. Blanking a list that already has
       rows made every refresh and every paid toggle flash the whole page away
       and back; with rows present the reload just dims. *@
    @if (_isLoading && !HasRows)
    {
        <p class="state">Loading bills…</p>
    }
    else if (!HasRows)
    {
        <p class="state">
            @if (HasNoBillsAtAll)
            {
                <span>No bills yet. Add one to get started.</span>
            }
            else
            {
                <span>No bills match the current filter.</span>
            }
        </p>
    }
    else
    {
        <div class="groups @(_isLoading ? "is-refreshing" : null)">
            @foreach (var section in Sections)
            {
                <BillGroup @key="section.Window"
                           Title="@section.Title"
                           Tone="@section.Tone"
                           Bills="@section.Bills"
                           Total="@section.Total"
                           Today="@_today"
                           BusyIds="@_busyIds"
                           OnTogglePaid="@TogglePaidAsync"
                           OnEdit="@OpenEditModal"
                           OnDelete="@OpenDeleteModal" />
            }
        </div>

        @if (_book.IsCapped)
        {
            @* A silent truncation would read as a complete book when it is not. *@
            <p class="capped">
                Showing the first @Rows.Count of @MatchCount matching bills — the
                sum above covers those @Rows.Count.
            </p>
        }
    }

</div>
```

- [ ] **Step 8: Rewrite the Bills code-behind**

Replace the entire contents of `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor.cs`:

```csharp
using BillsFrontEndBlazor.Models;
using BillsFrontEndBlazor.Services;
using BillsMinimalApi.Contracts;
using Microsoft.AspNetCore.Components;

namespace BillsFrontEndBlazor.Pages
{
    /// <summary>
    /// The bills page. It holds the whole book — filtered and searched by
    /// Postgres, then partitioned into due windows here — rather than one page at
    /// a time, because a due window spans the book and cannot be assembled from a
    /// slice of it.
    /// <para>
    /// The pager and the column sorts went with that change: the groups impose
    /// their own order, and a book sorted by amount has no due windows in it.
    /// </para>
    /// </summary>
    public partial class Bills : IDisposable
    {
        /// <summary>
        /// The most rows the page will hold at once.
        /// <para>
        /// Not a display limit — a real bound. Every row rendered into a Blazor
        /// Server circuit is state the server keeps and diffs on every change, so
        /// an unbounded book would make one large account slow for everybody
        /// sharing the host. When it bites, the page says so.
        /// </para>
        /// </summary>
        private const int RowCap = 500;

        /// <summary>
        /// How long the search box waits after the last keystroke. Long enough
        /// that typing a payee name is one query rather than eleven, short enough
        /// that it still feels like it is keeping up.
        /// </summary>
        private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

        /// <summary>The chips, in the order the design puts them.</summary>
        private static readonly BillStatus[] FilterOrder =
        {
            BillStatus.All,
            BillStatus.Unpaid,
            BillStatus.Overdue,
            BillStatus.Paid,
        };

        [Inject]
        public BillService BillService { get; set; } = default!;

        [Inject]
        public BillEventService BillEventService { get; set; } = default!;

        [Inject]
        public ToastService Toasts { get; set; } = default!;

        /// <summary>Set by the Overview's create link, which points at
        /// <c>bills?new=true</c>.</summary>
        [Parameter]
        [SupplyParameterFromQuery(Name = "new")]
        public bool OpenCreateForm { get; set; }

        /// <summary>
        /// Set by the Overview's "Clear the N late bills" link, which points at
        /// <c>bills?filter=overdue</c>. A string rather than a
        /// <see cref="BillStatus"/> so an unrecognised value falls back to All
        /// instead of failing to bind.
        /// </summary>
        [Parameter]
        [SupplyParameterFromQuery(Name = "filter")]
        public string? FilterName { get; set; }

        private BillBook _book = BillBook.Empty;
        private bool _isLoading = true;
        private bool _loadFailed;

        private string _searchText = string.Empty;
        private BillStatus _filter = BillStatus.All;

        /// <summary>
        /// Overdue bills across the whole table, not just what is loaded — the
        /// badge on the chip is a reason to click it, so counting only the rows on
        /// screen would defeat the point.
        /// </summary>
        private int _overdueCount;

        /// <summary>Every bill on the account, for the "N of M" tally.</summary>
        private int _billCount;

        /// <summary>
        /// The date the groups are cut against, fixed for the whole load so five
        /// sections cannot disagree about where the week ends. This is Blazor
        /// Server, so <see cref="DateTime.Today"/> is the API host's own date.
        /// </summary>
        private DateTime _today = DateTime.Today;

        /// <summary>Bills mid-flight in <see cref="TogglePaidAsync"/>. Keyed by id
        /// rather than one bool so a slow row cannot freeze the whole page.</summary>
        private readonly HashSet<long> _busyIds = new();

        /// <summary>
        /// Which load is the current one. A debounced keystroke, a chip click and
        /// a background refresh from <see cref="BillEventService"/> are routinely
        /// in flight together and do not come back in the order they were sent;
        /// without this a slow early response can land after a fast later one and
        /// put the page back to what you already stopped asking for.
        /// </summary>
        private int _loadGeneration;

        /// <summary>Cancels the pending debounce when another key is pressed.</summary>
        private CancellationTokenSource? _searchCts;

        // Modal state. The two "modals" are plain conditional rendering with
        // Bootstrap's classes — no bootstrap.bundle.js, no JS interop, which
        // matters because IJSRuntime is unusable during the prerender pass.
        private enum FormMode
        {
            None,
            Create,
            Edit,
        }

        private FormMode _formMode = FormMode.None;
        private Bill _formBill = new();
        private Bill? _deleteTarget;
        private bool _isSaving;

        // -- What the markup binds ---------------------------------------------

        /// <summary>One due-window section, ready to render.</summary>
        private sealed record BillSection(
            DueWindow Window,
            string Title,
            string Tone,
            List<Bill> Bills,
            decimal Total);

        private IReadOnlyList<Bill> Rows => _book.Bills;

        private bool HasRows => _book.Bills.Count > 0;

        /// <summary>How many match the chip and the search — which is not
        /// <c>Rows.Count</c> once the cap bites.</summary>
        private int MatchCount => _book.TotalCount;

        private int BillCount => _billCount;

        private int OverdueCount => _overdueCount;

        private bool HasSearch => !string.IsNullOrEmpty(_searchText);

        /// <summary>
        /// Distinguishes "you have no bills" from "nothing matches what you asked
        /// for" without a second request: a zero count while nothing is filtered
        /// or searched can only be the former.
        /// </summary>
        private bool HasNoBillsAtAll =>
            _book.TotalCount == 0 && _filter == BillStatus.All && !HasSearch;

        private decimal LoadedTotal => _book.Bills.Sum(b => b.PaymentDue);

        /// <summary>
        /// The five sections, empty ones dropped.
        /// <para>
        /// Computed per render rather than cached, so an optimistic paid toggle
        /// moves its row into the Paid group immediately instead of waiting for
        /// the reload. It is a partition of at most <see cref="RowCap"/> rows.
        /// </para>
        /// </summary>
        private IEnumerable<BillSection> Sections =>
            DueWindows.Order
                .Select(window => (
                    Window: window,
                    Bills: _book.Bills
                        .Where(b => DueWindows.Classify(b.Paid, b.DueDate, _today) == window)
                        // Soonest first within a section, which is the order the
                        // grouping exists to express. Id breaks ties so the list
                        // does not reshuffle between renders.
                        .OrderBy(b => b.DueDate ?? DateTime.MaxValue)
                        .ThenBy(b => b.Id)
                        .ToList()))
                .Where(g => g.Bills.Count > 0)
                .Select(g => new BillSection(
                    g.Window,
                    DueWindows.Title(g.Window),
                    Tone(g.Window),
                    g.Bills,
                    g.Bills.Sum(b => b.PaymentDue)));

        /// <summary>The dot colour per section. Tokens only — the component it is
        /// handed to never names a palette.</summary>
        private static string Tone(DueWindow window) => window switch
        {
            DueWindow.Late => "var(--late)",
            DueWindow.ThisWeek => "var(--accent)",
            DueWindow.ThisMonth => "var(--text)",
            DueWindow.Later => "var(--muted)",
            _ => "var(--ok)",
        };

        // -- Controls -----------------------------------------------------------

        private Task SetFilterAsync(BillStatus filter)
        {
            if (_filter == filter)
            {
                return Task.CompletedTask;
            }

            _filter = filter;

            return LoadBillsAsync();
        }

        /// <summary>
        /// Waits out <see cref="SearchDebounce"/> before asking the server, and
        /// abandons the wait if another key arrives.
        /// </summary>
        private async Task OnSearchInputAsync(ChangeEventArgs e)
        {
            var next = e.Value?.ToString() ?? string.Empty;

            if (_searchText == next)
            {
                return;
            }

            _searchText = next;

            // Cancel first, then dispose: Cancel runs the delay's registration
            // synchronously, so by the time it returns there is nothing left
            // holding the source.
            _searchCts?.Cancel();
            _searchCts?.Dispose();

            var cts = new CancellationTokenSource();
            _searchCts = cts;

            try
            {
                await Task.Delay(SearchDebounce, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Another key arrived; that keystroke owns the request now.
                return;
            }

            await LoadBillsAsync();
        }

        private Task ClearSearchAsync()
        {
            if (!HasSearch)
            {
                return Task.CompletedTask;
            }

            // No debounce on the way back to empty: pressing Clear is a decision,
            // not a keystroke on the way to one.
            _searchCts?.Cancel();
            _searchText = string.Empty;

            return LoadBillsAsync();
        }

        // -- Loading ------------------------------------------------------------

        protected override async Task OnInitializedAsync()
        {
            BillEventService.OnBillsChanged += OnBillsChanged;

            // Here rather than in OnParametersSet: this runs exactly once per
            // component instance, so a deep link cannot re-apply its filter — or
            // pop the form open again — on some later re-render while the user is
            // part-way through changing it.
            ApplyQueryFilter();

            if (OpenCreateForm)
            {
                OpenCreateModal();
            }

            await LoadBillsAsync();
        }

        /// <summary>
        /// Honours <c>?filter=overdue</c>. Case-insensitive because the link is
        /// written in a URL, and validated because anyone can type anything into
        /// one — an unrecognised value leaves the chip on All rather than
        /// throwing.
        /// </summary>
        private void ApplyQueryFilter()
        {
            if (Enum.TryParse<BillStatus>(FilterName, ignoreCase: true, out var status)
                && Enum.IsDefined(status))
            {
                _filter = status;
            }
        }

        public void Dispose()
        {
            BillEventService.OnBillsChanged -= OnBillsChanged;

            _searchCts?.Cancel();
            _searchCts?.Dispose();
        }

        private void OnBillsChanged()
        {
            // Never `async void`: the handler is invoked from whatever thread
            // raised the event, and an unhandled exception there has no
            // SynchronizationContext to marshal it back to the circuit.
            _ = InvokeAsync(LoadBillsAsync);
        }

        /// <summary>
        /// Turns the current state of the page into one request. Deliberately the
        /// only place that happens, so the chips, the search box and the tally
        /// cannot end up describing different queries.
        /// </summary>
        private BillQuery BuildQuery() => new(
            Page: 1,
            PageSize: BillQuery.MaxPageSize,
            Search: _searchText,
            Status: _filter,
            // The groups re-sort within themselves anyway; asking the server for
            // due-date order means the cap keeps the soonest bills rather than an
            // arbitrary 500.
            Sort: BillSort.DueDate,
            Descending: false,
            From: null,
            To: null);

        /// <summary>
        /// Kept parameterless so the markup can still write
        /// <c>@onclick="LoadBillsAsync"</c> — an optional parameter would break
        /// the method-group conversion the event binding relies on.
        /// </summary>
        private async Task LoadBillsAsync()
        {
            var generation = ++_loadGeneration;

            _isLoading = true;
            _loadFailed = false;
            _today = DateTime.Today;
            StateHasChanged();

            try
            {
                // Concurrently: the two counts ask different questions from the
                // rows — every overdue bill and every bill, regardless of what is
                // filtered or searched — so neither can be derived from them, but
                // neither need wait for them either.
                var book = BillService.GetBookAsync(BuildQuery(), RowCap);
                var overdue = BillService.CountAsync(BillStatus.Overdue);
                var everything = BillService.CountAsync(BillStatus.All);

                await Task.WhenAll(book, overdue, everything);

                if (generation != _loadGeneration)
                {
                    return;
                }

                _book = book.Result;
                _overdueCount = overdue.Result;
                _billCount = everything.Result;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // In Blazor Server an unhandled exception kills the circuit and
                // replaces the page with the yellow error bar — very likely on
                // first load if the blazor container outruns the api container.
                if (generation != _loadGeneration)
                {
                    return;
                }

                _book = BillBook.Empty;
                _overdueCount = 0;
                _billCount = 0;
                _loadFailed = true;
                Toasts.ShowError("Could not load bills. Is the API running?");
            }
            finally
            {
                // A superseded load must not clear the spinner: the load that
                // replaced it is still running, and the page would flash from
                // dimmed to crisp and back.
                if (generation == _loadGeneration)
                {
                    _isLoading = false;
                    StateHasChanged();
                }
            }
        }

        // -- Writes -------------------------------------------------------------

        private void OpenCreateModal()
        {
            _formBill = new Bill { DueDate = DateTime.Today };
            _formMode = FormMode.Create;
        }

        private void OpenEditModal(Bill bill)
        {
            // A copy, not the page's instance: cancelling the form must not leave
            // half-typed values rendered in the row behind it.
            _formBill = new Bill
            {
                Id = bill.Id,
                PayeeName = bill.PayeeName,
                DueDate = bill.DueDate,
                PaymentDue = bill.PaymentDue,
                Paid = bill.Paid,
                Version = bill.Version,
            };

            _formMode = FormMode.Edit;
        }

        private void OpenDeleteModal(Bill bill) => _deleteTarget = bill;

        private void CloseForm()
        {
            _formMode = FormMode.None;
            _isSaving = false;
        }

        private void CloseDelete()
        {
            _deleteTarget = null;
            _isSaving = false;
        }

        /// <summary>
        /// Marks a bill paid or unpaid straight from its row. Ticking a box is the
        /// most common thing anyone does on this page, and routing it through the
        /// edit modal meant opening a form, changing one checkbox, and submitting
        /// five fields back.
        /// </summary>
        private async Task TogglePaidAsync(Bill bill)
        {
            // Add returns false if the id is already in the set, which is what
            // stops a double-click sending two writes.
            if (!_busyIds.Add(bill.Id))
            {
                return;
            }

            // Optimistic: flip now and put it back if the write fails. Sections is
            // computed per render, so the row moves to its new group immediately.
            bill.Paid = !bill.Paid;

            try
            {
                var result = await BillService.UpdateBillAsync(bill);

                if (!result.Success)
                {
                    bill.Paid = !bill.Paid;
                    Toasts.ShowError(result.ToMessage("update"));

                    // Our copy is stale (409) or the row is gone (404); either way
                    // what is on screen is wrong, so resync.
                    if (result.IsConflict || result.IsNotFound)
                    {
                        AfterWrite();
                    }

                    return;
                }

                Toasts.ShowSuccess(bill.Paid ? "Marked as paid" : "Marked as unpaid");

                // Not merely to refresh the Overview: the API increments Version
                // on every write, so the copy in this list is now stale and a
                // second toggle would 409 against it.
                AfterWrite();
            }
            finally
            {
                _busyIds.Remove(bill.Id);
            }
        }

        private async Task SaveFormAsync()
        {
            if (_isSaving)
            {
                return;
            }

            _isSaving = true;

            try
            {
                var creating = _formMode == FormMode.Create;

                var result = creating
                    ? await BillService.CreateBillAsync(_formBill)
                    : await BillService.UpdateBillAsync(_formBill);

                if (!result.Success)
                {
                    Toasts.ShowError(result.ToMessage(creating ? "create" : "update"));

                    // A 409 means someone else won the race and our copy is stale,
                    // and a 404 means the row is gone — in both cases the list on
                    // screen is wrong, so refresh it before the retry. The form
                    // stays open with the user's values intact.
                    if (result.IsConflict || result.IsNotFound)
                    {
                        AfterWrite();
                    }

                    return;
                }

                Toasts.ShowSuccess(creating ? "Bill created" : "Bill updated");
                CloseForm();
                AfterWrite();
            }
            finally
            {
                _isSaving = false;
            }
        }

        private async Task ConfirmDeleteAsync()
        {
            if (_deleteTarget is not { } bill || _isSaving)
            {
                return;
            }

            _isSaving = true;

            try
            {
                var result = await BillService.DeleteBillAsync(bill.Id);

                if (!result.Success)
                {
                    Toasts.ShowError(result.ToMessage("delete"));

                    if (result.IsNotFound)
                    {
                        CloseDelete();
                        AfterWrite();
                    }

                    return;
                }

                Toasts.ShowSuccess("Bill deleted");
                CloseDelete();
                AfterWrite();
            }
            finally
            {
                _isSaving = false;
            }
        }

        private void AfterWrite()
        {
            // Publishing is enough: this page subscribes too, so the Overview
            // recomputes and this list reloads from the one notification — no
            // double fetch. Scoped per circuit, so it never reaches another
            // connected browser.
            BillEventService.NotifyBillsChanged();
        }
    }
}
```

- [ ] **Step 9: Style the page**

Create `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor.css`:

```css
.bills {
    display: flex;
    flex-direction: column;
    gap: 1rem;
    margin: 0 auto;
    max-width: 1200px;
    padding: 1.5rem;
}

.page-head {
    align-items: flex-start;
    display: flex;
    justify-content: space-between;
}

.page-head h1 {
    color: var(--text);
    font-size: 1.4rem;
    font-weight: 600;
    margin: 0;
}

.lede {
    color: var(--faint);
    font-size: .82rem;
    margin: .1rem 0 0;
}

.head-actions {
    display: flex;
    gap: .5rem;
}

.ghost,
.primary {
    align-items: center;
    background: none;
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius);
    color: var(--muted);
    cursor: pointer;
    display: inline-flex;
    padding: .4rem .8rem;
}

/* Outline, not a flood fill: the accent is a line colour in this palette. */
.primary {
    border-color: var(--accent);
    color: var(--accent-text);
}

.ghost:hover:not(:disabled) { color: var(--text); }

.ghost:disabled,
.primary:disabled {
    cursor: default;
    opacity: .5;
}

.controls {
    align-items: center;
    display: flex;
    flex-wrap: wrap;
    gap: .75rem;
}

.chips {
    display: flex;
    gap: .35rem;
}

.chip {
    background: none;
    border: var(--border-width) solid var(--border);
    border-radius: 999px;
    color: var(--muted);
    cursor: pointer;
    font-size: .82rem;
    padding: .25rem .75rem;
}

.chip.active {
    border-color: var(--accent);
    color: var(--accent-text);
}

.chip-badge {
    color: var(--late);
    margin-left: .3rem;
}

.search {
    align-items: center;
    display: flex;
    gap: .5rem;
    position: relative;
}

.search input {
    background: var(--sunken);
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius);
    color: var(--text);
    padding: .4rem .7rem .4rem 2rem;
    width: 16rem;
}

::deep .search-icon {
    color: var(--faint);
    left: .6rem;
    pointer-events: none;
    position: absolute;
}

.tally {
    color: var(--muted);
    font-variant-numeric: tabular-nums;
    margin: 0 0 0 auto;
}

.state,
.capped {
    color: var(--muted);
    margin: 0;
    padding: 1rem 0;
}

.groups {
    display: flex;
    flex-direction: column;
    gap: 1rem;
    transition: opacity .15s ease;
}

/* Dim rather than blank: the rows on screen are still the right rows, they are
   just about to be replaced by fresher copies of themselves. */
.groups.is-refreshing {
    opacity: .55;
}
```

- [ ] **Step 10: Run the whole suite**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln`
Expected: PASS. The Blazor project builds here too, so a leftover reference to `PagedBills`, `SortCaret` or `GoToPageAsync` fails the run.

- [ ] **Step 11: Verify it by eye**

Run the app and check Bills:
1. Sections appear in order — Late, Due this week, Due this month, Later, Paid — and empty ones are absent.
2. Each header's count and sum match its rows.
3. The chips narrow the set and the groups partition what survives: Overdue leaves one Late section, Paid leaves one Paid section.
4. From the Overview, "Clear the N late bills" lands here with Overdue selected and exactly the bills the sentence counted.
5. Toggling a bill paid moves it into the Paid section without a page flash.
6. Search still debounces — one request per pause, not per keystroke.

- [ ] **Step 12: Commit**

```bash
git add -A bills-frontend/BillsFrontEndBlazor BillsMinimalApi.Contracts/DueWindows.cs tests/BillsMinimalApi.UnitTests/DueWindowsTests.cs
git commit -m "Group bills by when they fall due instead of by page"
```

---

### Task 10: Bulk select, bulk mark paid

Idea 6. Row checkboxes, and a sticky bar that says how many are picked and what they come to.

The whole point is the Late group: eight bills, eight clicks, eight round trips and eight re-renders becomes one gesture. So the bar has to be honest about a batch that only partly lands — the writes that succeeded are committed and cannot be taken back, and a plain "something went wrong" would leave you re-marking bills that are already paid.

**Files:**
- Create: `BillsMinimalApi.Contracts/BulkPaidOutcome.cs`
- Create: `tests/BillsMinimalApi.UnitTests/BulkPaidOutcomeTests.cs`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/BulkActionBar.razor`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/BulkActionBar.razor.css`
- Modify: `bills-frontend/BillsFrontEndBlazor/Services/BillService.cs` (add `BulkPaidResult` after `BillWriteResult`, and `MarkManyPaidAsync` after `MarkPaidAsync` from Task 8)
- Modify: `bills-frontend/BillsFrontEndBlazor/Shared/BillGroup.razor` (a checkbox cell and two parameters)
- Modify: `bills-frontend/BillsFrontEndBlazor/Shared/BillGroup.razor.css` (one grid template, one selected-row rule)
- Modify: `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor` (pass selection down, render the bar)
- Modify: `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor.cs` (selection state, the bulk write, pruning)

**Interfaces:**
- Consumes: `BillGroup` (Task 9), `BillService.UpdateBillAsync` (existing, line 217).
- Produces:
  - `BulkPaidOutcome(int Succeeded, int Failed)` with `Total`, `AllSucceeded`, `Describe(string? reason = null) -> string`
  - `BulkPaidResult(BulkPaidOutcome Outcome, HttpStatusCode? FirstFailure)` with `Success`, `ToMessage()`
  - `BillService.MarkManyPaidAsync(IReadOnlyList<Bill> bills, CancellationToken ct = default) -> Task<BulkPaidResult>`
  - `<BulkActionBar Count Total PayableCount Busy OnMarkPaid OnClear />`
  - `BillGroup` gains `SelectedIds` and `OnToggleSelected` — Task 11 replaces the date and amount cells in the same row template.

- [ ] **Step 1: Write the failing outcome tests**

Create `tests/BillsMinimalApi.UnitTests/BulkPaidOutcomeTests.cs`:

```csharp
using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// What the app says after marking a batch of bills paid.
/// <para>
/// Worth pinning rather than eyeballing: a bulk write is the one place where
/// "it worked" and "it failed" are both wrong answers, and the sentence has to
/// pluralise, count, and stay a sentence when there is no reason to append.
/// </para>
/// </summary>
public sealed class BulkPaidOutcomeTests
{
    [Fact]
    public void One_bill_is_singular()
    {
        Assert.Equal("Marked 1 bill as paid", new BulkPaidOutcome(1, 0).Describe());
    }

    [Fact]
    public void Several_bills_are_plural()
    {
        Assert.Equal("Marked 3 bills as paid", new BulkPaidOutcome(3, 0).Describe());
    }

    [Fact]
    public void A_partial_batch_reports_both_halves()
    {
        // The two numbers are the point. "Something went wrong" would leave you
        // re-marking the two that already went through.
        Assert.Equal(
            "Marked 2 of 3 as paid — 1 could not be saved",
            new BulkPaidOutcome(2, 1).Describe());
    }

    [Fact]
    public void A_batch_that_wholly_failed_does_not_claim_a_partial_success()
    {
        Assert.Equal(
            "Could not mark that bill as paid",
            new BulkPaidOutcome(0, 1).Describe());

        Assert.Equal(
            "Could not mark any of those 4 bills as paid",
            new BulkPaidOutcome(0, 4).Describe());
    }

    [Fact]
    public void An_empty_batch_does_not_congratulate_itself()
    {
        // Guards the reading that makes 0 successes and 0 failures a clean run:
        // "Marked 0 bills as paid" after clicking a disabled-looking button is
        // worse than saying nothing happened.
        var outcome = new BulkPaidOutcome(0, 0);

        Assert.Equal("Nothing to mark as paid", outcome.Describe());
        Assert.False(outcome.AllSucceeded);
    }

    [Fact]
    public void A_reason_is_appended_as_its_own_sentence()
    {
        Assert.Equal(
            "Marked 2 of 3 as paid — 1 could not be saved. Another change landed first.",
            new BulkPaidOutcome(2, 1).Describe("Another change landed first."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_reason_leaves_no_dangling_punctuation(string? reason)
    {
        // A trailing ". " reads as a truncated message, which is exactly what a
        // failed write should not look like.
        Assert.Equal("Marked 1 bill as paid", new BulkPaidOutcome(1, 0).Describe(reason));
    }

    [Fact]
    public void All_succeeded_means_something_succeeded_and_nothing_failed()
    {
        Assert.True(new BulkPaidOutcome(2, 0).AllSucceeded);
        Assert.False(new BulkPaidOutcome(2, 1).AllSucceeded);
        Assert.False(new BulkPaidOutcome(0, 0).AllSucceeded);
    }

    [Fact]
    public void Total_counts_everything_that_was_attempted()
    {
        Assert.Equal(5, new BulkPaidOutcome(3, 2).Total);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter BulkPaidOutcomeTests`
Expected: FAIL — `The type or namespace name 'BulkPaidOutcome' could not be found`.

- [ ] **Step 3: Write the outcome type**

Create `BillsMinimalApi.Contracts/BulkPaidOutcome.cs`:

```csharp
namespace BillsMinimalApi.Contracts;

/// <summary>
/// How a batch of mark-paid writes went, and how to say so.
/// <para>
/// In the contracts project rather than beside the service that produces it,
/// because the sentence is the part worth testing and the service is not
/// reachable from a unit test. Named for the one operation it describes: there
/// is exactly one bulk action in this app, and a general "bulk outcome"
/// abstraction for a single caller would be inventing a requirement.
/// </para>
/// </summary>
public readonly record struct BulkPaidOutcome(int Succeeded, int Failed)
{
    public int Total => Succeeded + Failed;

    /// <summary>
    /// Something was written and nothing was refused. Zero of each is not
    /// success — it is a batch that never happened, which the caller should not
    /// report as a win.
    /// </summary>
    public bool AllSucceeded => Failed == 0 && Succeeded > 0;

    /// <param name="reason">
    /// Why the batch was not clean — a whole sentence, appended after the
    /// headline. Null or empty appends nothing, so a clean run does not end in
    /// stray punctuation.
    /// </param>
    public string Describe(string? reason = null)
    {
        var headline = (Succeeded, Failed) switch
        {
            (0, 0) => "Nothing to mark as paid",
            (_, 0) => $"Marked {Succeeded} {(Succeeded == 1 ? "bill" : "bills")} as paid",
            (0, 1) => "Could not mark that bill as paid",
            (0, _) => $"Could not mark any of those {Failed} bills as paid",

            // Both numbers, always. The successful writes are committed and
            // cannot be taken back, so a message that hides them sends you back
            // to re-mark bills that are already paid.
            _ => $"Marked {Succeeded} of {Total} as paid — {Failed} could not be saved",
        };

        return string.IsNullOrEmpty(reason) ? headline : $"{headline}. {reason}";
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter BulkPaidOutcomeTests`
Expected: PASS, 10 tests.

- [ ] **Step 5: Teach the service to write a batch**

Add to `bills-frontend/BillsFrontEndBlazor/Services/BillService.cs`, immediately after the `BillWriteResult` record (line 38) and before `public class BillService`:

```csharp
    /// <summary>
    /// Outcome of a batch of writes: the counts, plus the status of the first
    /// one that failed — enough to say why without claiming every failure had
    /// the same cause.
    /// </summary>
    public sealed record BulkPaidResult(BulkPaidOutcome Outcome, HttpStatusCode? FirstFailure)
    {
        public bool Success => Outcome.AllSucceeded;

        public string ToMessage() => Outcome.Describe(Reason);

        private string? Reason => Outcome.AllSucceeded
            ? null
            : FirstFailure switch
            {
                // The list is reloaded after every batch, so the fix is already
                // done by the time this is read — saying so stops the reflex to
                // hit refresh and try the whole thing again.
                HttpStatusCode.Conflict =>
                    "Another change landed first — the list has been refreshed.",
                HttpStatusCode.NotFound =>
                    "They were already gone — the list has been refreshed.",
                HttpStatusCode.Unauthorized =>
                    "Your session has expired. Reload the page to sign in again.",
                _ => "Is the API running?",
            };
    }
```

Add to the same file, after `MarkPaidAsync` (added in Task 8):

```csharp
        /// <summary>
        /// Marks each of <paramref name="bills"/> paid, and reports how many
        /// landed.
        /// <para>
        /// One request per bill, because the API has no batch endpoint and adding
        /// one would mean inventing a partial-failure semantics on the server
        /// too — a 207 nobody else consumes. The concurrency token on each row
        /// still does its job this way: a bill someone else changed comes back
        /// 409 and is counted as a failure rather than being silently overwritten.
        /// </para>
        /// <para>
        /// Sequential rather than concurrent, deliberately. The API rate-limits
        /// per client, so firing fifty writes at once is the one thing guaranteed
        /// to turn a working batch into a wall of 429s — and a queue of one makes
        /// the failure counts mean what they say.
        /// </para>
        /// <para>
        /// Note that a batch is not a transaction. Writes that succeed before a
        /// failure stay written, which is exactly why the result carries counts
        /// instead of a bool.
        /// </para>
        /// </summary>
        /// <param name="bills">
        /// Bills to mark paid. Already-paid bills should be filtered out by the
        /// caller — sending them would spend a request to write what is already
        /// there.
        /// </param>
        public async Task<BulkPaidResult> MarkManyPaidAsync(
            IReadOnlyList<Bill> bills,
            CancellationToken ct = default)
        {
            var succeeded = 0;
            var failed = 0;
            HttpStatusCode? firstFailure = null;

            foreach (var bill in bills)
            {
                // A copy, not the caller's instance. The page still has these
                // rendered; flipping Paid here would move rows between groups
                // one at a time as the batch ran, and leave them moved if the
                // write was refused.
                var payload = new Bill
                {
                    Id = bill.Id,
                    PayeeName = bill.PayeeName,
                    DueDate = bill.DueDate,
                    PaymentDue = bill.PaymentDue,
                    Paid = true,
                    Version = bill.Version,
                };

                var result = await UpdateBillAsync(payload, ct);

                if (result.Success)
                {
                    succeeded++;
                    continue;
                }

                failed++;

                // First, not last: the first refusal is the one that explains the
                // batch — later ones are often knock-on effects of the same
                // expired session or the same stale list.
                firstFailure ??= result.Status;
            }

            return new BulkPaidResult(new BulkPaidOutcome(succeeded, failed), firstFailure);
        }
```

- [ ] **Step 6: Write the action bar**

Create `bills-frontend/BillsFrontEndBlazor/Shared/BulkActionBar.razor`:

```razor
@* Idea 6. Sticky rather than fixed: it belongs to the list, so it rides at the
   bottom of the viewport while the list is long and settles at the end of it
   when the list is short. *@
<div class="bar" role="status">

    <span class="label">@Count @(Count == 1 ? "bill" : "bills") selected</span>

    <span class="total">@Total.ToString("C")</span>

    <button type="button"
            class="pay"
            disabled="@(Busy || PayableCount == 0)"
            title="@(PayableCount == 0 ? "Every selected bill is already paid" : null)"
            @onclick="OnMarkPaid">
        @if (Busy)
        {
            <span class="spinner-border spinner-border-sm me-1" role="status"></span>
            <span>Marking…</span>
        }
        else
        {
            <span>Mark paid</span>
        }
    </button>

    @* Not disabled while the batch runs: abandoning a selection you no longer
       want is always safe, and the writes already in flight are unaffected. *@
    <button type="button" class="clear" @onclick="OnClear">Clear</button>

</div>

@code {
    [Parameter, EditorRequired]
    public int Count { get; set; }

    [Parameter, EditorRequired]
    public decimal Total { get; set; }

    /// <summary>
    /// How many of the selected bills are actually unpaid. Separate from
    /// <see cref="Count"/> so the button can go dead rather than spend a round
    /// trip writing Paid = true over Paid = true.
    /// </summary>
    [Parameter]
    public int PayableCount { get; set; }

    [Parameter]
    public bool Busy { get; set; }

    [Parameter]
    public EventCallback OnMarkPaid { get; set; }

    [Parameter]
    public EventCallback OnClear { get; set; }
}
```

Create `bills-frontend/BillsFrontEndBlazor/Shared/BulkActionBar.razor.css`:

```css
.bar {
    align-items: center;
    background: var(--surface);
    border: var(--border-width) solid var(--accent);
    border-radius: var(--radius-lg);
    bottom: 1rem;
    display: flex;
    gap: .85rem;
    padding: .7rem 1rem;
    position: sticky;
}

.label {
    color: var(--text);
    font-size: .85rem;
    font-weight: 500;
    white-space: nowrap;
}

/* Pushes both buttons to the right edge. */
.total {
    color: var(--muted);
    font-size: .85rem;
    font-variant-numeric: tabular-nums;
    margin-right: auto;
}

.pay,
.clear {
    align-items: center;
    background: none;
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius);
    color: var(--muted);
    cursor: pointer;
    display: inline-flex;
    flex: none;
    font-size: .8rem;
    padding: .35rem .75rem;
    white-space: nowrap;
}

/* Outline only, in this palette as in every other. */
.pay {
    border-color: var(--accent);
    color: var(--accent-text);
}

.pay:disabled {
    border-color: var(--border);
    color: var(--faint);
    cursor: default;
}
```

- [ ] **Step 7: Add the checkbox to the row**

In `bills-frontend/BillsFrontEndBlazor/Shared/BillGroup.razor`, insert this as the first child of the `<li>`, immediately above `<span class="payee">@bill.PayeeName</span>`:

```razor
                @* One-way `checked` with @onchange rather than @bind, because
                   the selection lives on the page and not here. Safe only
                   because the handler always changes the set: were it ever a
                   no-op, the browser would keep the box the user clicked and
                   Blazor would see no state change to correct it with. *@
                <input type="checkbox"
                       class="pick"
                       aria-label="Select @bill.PayeeName"
                       checked="@SelectedIds.Contains(bill.Id)"
                       @onchange="@(() => OnToggleSelected.InvokeAsync(bill))" />

```

In the same file, change the opening `<li>` tag from:

```razor
            <li @key="bill.Id" class="@(IsOverdue(bill, Today) ? "overdue" : null)">
```

to:

```razor
            <li @key="bill.Id"
                class="@(IsOverdue(bill, Today) ? "overdue" : null) @(SelectedIds.Contains(bill.Id) ? "selected" : null)">
```

And add these two parameters to its `@code` block, after `BusyIds`:

```csharp
    /// <summary>Ids of the checked rows. Owned by the page, because the action
    /// bar it feeds is outside this component.</summary>
    [Parameter]
    public IReadOnlySet<long> SelectedIds { get; set; } = new HashSet<long>();

    [Parameter]
    public EventCallback<Bill> OnToggleSelected { get; set; }
```

- [ ] **Step 8: Make room for it in the grid**

In `bills-frontend/BillsFrontEndBlazor/Shared/BillGroup.razor.css`, replace the `.rows li` rule and its comment:

```css
/* Five fixed columns and one flexible one, so the payee takes the slack and
   every number below it stays in a line. Task 10 adds a checkbox column to the
   front of this template. */
.rows li {
    align-items: center;
    border-top: var(--border-width) solid var(--border);
    display: grid;
    gap: 1rem;
    grid-template-columns: 1fr 11rem 7rem 5.5rem auto;
    padding: .5rem 0;
}
```

with:

```css
/* Checkbox, then five fixed columns and one flexible one, so the payee takes
   the slack and every number below it stays in a line. */
.rows li {
    align-items: center;
    border-top: var(--border-width) solid var(--border);
    display: grid;
    gap: 1rem;
    grid-template-columns: 1.1rem 1fr 11rem 7rem 5.5rem auto;
    padding: .5rem 0;
}

/* Bleeds past the card's padding so a selected row reads as a band across the
   whole section rather than an indented stripe. */
.rows li.selected {
    background: var(--sunken);
    box-shadow: -1.25rem 0 0 var(--sunken), 1.25rem 0 0 var(--sunken);
}

.pick {
    accent-color: var(--accent);
    cursor: pointer;
    margin: 0;
}
```

- [ ] **Step 9: Wire the page up**

In `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor`, add the two selection parameters to the `<BillGroup>` call — replace:

```razor
                           BusyIds="@_busyIds"
                           OnTogglePaid="@TogglePaidAsync"
```

with:

```razor
                           BusyIds="@_busyIds"
                           SelectedIds="@_selectedIds"
                           OnToggleSelected="@ToggleSelected"
                           OnTogglePaid="@TogglePaidAsync"
```

Then add the bar as the last child of `<div class="bills">`, after the whole `@if (_isLoading && !HasRows) … else { … }` chain and immediately before the closing `</div>`:

```razor
    @* Outside the groups block on purpose: the selection is pruned to loaded
       rows on every load, so if this is showing anything there are rows behind
       it to show. *@
    @if (SelectedCount > 0)
    {
        <BulkActionBar Count="@SelectedCount"
                       Total="@SelectedTotal"
                       PayableCount="@PayableCount"
                       Busy="@_isBulkWriting"
                       OnMarkPaid="@MarkSelectedPaidAsync"
                       OnClear="@ClearSelection" />
    }
```

- [ ] **Step 10: Add the selection state and the bulk write**

In `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor.cs`, add these two fields immediately after the `_busyIds` field:

```csharp
        /// <summary>
        /// Ids of the checked rows.
        /// <para>
        /// Ids rather than <see cref="Bill"/> instances: every load replaces the
        /// list with fresh objects carrying fresh concurrency tokens, and a set
        /// of stale references would quietly hold the old ones.
        /// </para>
        /// </summary>
        private readonly HashSet<long> _selectedIds = new();

        private bool _isBulkWriting;
```

Add these members after the `Tone` method:

```csharp
        private int SelectedCount => _selectedIds.Count;

        /// <summary>
        /// The selected bills, resolved against what is loaded. Safe to sum
        /// because <see cref="LoadBillsAsync"/> prunes the selection to the rows
        /// on screen — an id in the set is always an id in the list.
        /// </summary>
        private IEnumerable<Bill> SelectedBills =>
            _book.Bills.Where(b => _selectedIds.Contains(b.Id));

        private decimal SelectedTotal => SelectedBills.Sum(b => b.PaymentDue);

        /// <summary>How many of the selection there is anything to do to.</summary>
        private int PayableCount => SelectedBills.Count(b => !b.Paid);

        private void ToggleSelected(Bill bill)
        {
            // Remove returns false when it was not there, which is the cheapest
            // correct way to write "toggle".
            if (!_selectedIds.Remove(bill.Id))
            {
                _selectedIds.Add(bill.Id);
            }
        }

        private void ClearSelection() => _selectedIds.Clear();

        /// <summary>
        /// Marks every unpaid bill in the selection as paid. The point of the
        /// whole idea: eight late bills in one gesture rather than eight clicks,
        /// eight round trips and eight re-renders.
        /// </summary>
        private async Task MarkSelectedPaidAsync()
        {
            if (_isBulkWriting)
            {
                return;
            }

            // Materialised before the awaits: SelectedBills is a live query over
            // state that the reload at the end of this method replaces.
            var payable = SelectedBills.Where(b => !b.Paid).ToList();

            if (payable.Count == 0)
            {
                return;
            }

            _isBulkWriting = true;

            try
            {
                var result = await BillService.MarkManyPaidAsync(payable);

                if (result.Success)
                {
                    Toasts.ShowSuccess(result.ToMessage());
                }
                else
                {
                    Toasts.ShowError(result.ToMessage());
                }

                // Cleared whatever happened. The successful writes are committed,
                // so leaving the batch selected invites a second run at bills that
                // are already paid — and the message has already said how many did
                // not land.
                _selectedIds.Clear();

                // Not merely to refresh the Overview: every bill written now has a
                // higher Version, so the copies in this list are stale.
                AfterWrite();
            }
            finally
            {
                _isBulkWriting = false;
            }
        }
```

In `LoadBillsAsync`, add the prune immediately after `_billCount = everything.Result;`:

```csharp
                // Drop anything that is no longer on screen — a different chip, a
                // narrower search, a deleted bill, or a row past the cap. The bar
                // reports a count and a total, and both would be lies if the set
                // could hold bills the page cannot show.
                //
                // Pruning rather than clearing outright, which is what the design
                // prototype did on every filter change: a selection that survives
                // narrowing the list is the more useful of the two, and pruning is
                // needed here anyway for deletes and the cap.
                _selectedIds.IntersectWith(_book.Bills.Select(b => b.Id));
```

And in the same method's `catch` block, add after `_billCount = 0;`:

```csharp
                _selectedIds.Clear();
```

- [ ] **Step 11: Run the whole suite**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln`
Expected: PASS. `BulkPaidOutcomeTests` is 10 of them, and the Blazor project builds here too.

- [ ] **Step 12: Verify it by eye**

Run the app and go to Bills:
1. Check three rows across two groups — the bar appears, reads "3 bills selected", and its total matches the three amounts.
2. Uncheck one — the count, the total and the row highlight all follow.
3. Press "Mark paid" — the button shows "Marking…", then a success toast, the selection clears, and the rows land in the Paid group.
4. Select only bills that are already paid — the button is dead and its tooltip says why.
5. Select a bill, then switch the chip to a filter that excludes it — the bar disappears rather than counting a bill you cannot see.
6. Stop the API and press "Mark paid" — an error toast that names the counts, not a torn-down circuit.

- [ ] **Step 13: Commit**

```bash
git add -A bills-frontend/BillsFrontEndBlazor BillsMinimalApi.Contracts/BulkPaidOutcome.cs tests/BillsMinimalApi.UnitTests/BulkPaidOutcomeTests.cs
git commit -m "Clear a batch of late bills in one gesture"
```

---

### Task 11: Edit where the value sits, and keep the modal for creating only

Idea 7. Click a payee, a date or an amount and change it there. The modal survives only for a bill you are making from nothing.

**Two deliberate departures, both named here so nobody has to guess:**

1. **The spec lists inline edit "on dates and amounts". This adds the payee as well.** Removing the edit modal removes the only way to rename a payee, and shipping a redesign that quietly drops "fix a typo in a payee name" is not a redesign. `Paid` needs nothing: the status button already toggles it, and the delete modal already covers the fourth verb. With payee, date and amount editable in place, the edit modal is genuinely redundant rather than merely unfashionable.
2. **The spec calls the copy *"modal footer copy"*; the prototype puts it at the foot of the Bills page**, as a note under the last group with "Open the create form" as an underlined link. That is where it makes sense — it explains where the edit form went, so it has to be readable without opening a modal. It goes at the foot of the page.

**Files:**
- Create: `BillsMinimalApi.Contracts/InlineEditValues.cs`
- Create: `tests/BillsMinimalApi.UnitTests/InlineEditValuesTests.cs`
- Create: `bills-frontend/BillsFrontEndBlazor/Models/BillEdit.cs`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/InlineEdit.razor`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/InlineEdit.razor.css`
- Modify: `bills-frontend/BillsFrontEndBlazor/Shared/BillGroup.razor` (three cells become editors, the pencil goes, `OnEdit` becomes `OnFieldEdited`)
- Modify: `bills-frontend/BillsFrontEndBlazor/Shared/BillGroup.razor.css` (drop the cell paddings the editors now own)
- Modify: `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor` (the create/edit modal becomes create-only; the footnote arrives)
- Modify: `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor.cs` (`FormMode` collapses to a bool; `SaveEditAsync` arrives)

**Interfaces:**
- Consumes: `BillGroup` (Tasks 9–10), `BillService.UpdateBillAsync` (existing, line 217).
- Produces:
  - `InlineEditValues.TryParseDate(string? raw, out DateTime value) -> bool`
  - `InlineEditValues.TryParseAmount(string? raw, out decimal value) -> bool`
  - `InlineEditValues.TryParsePayee(string? raw, out string value) -> bool`
  - `InlineEditValues.MinimumAmount -> decimal` (const `0.01m`)
  - `enum InlineEditKind { Text, Date, Amount }`
  - `BillEdit(Bill Bill, Action<Bill> Apply)`
  - `<InlineEdit Kind Display Label Payee Date Amount Disabled PayeeCommitted DateCommitted AmountCommitted />`
  - `BillGroup` gains `OnFieldEdited` (`EventCallback<BillEdit>`) and loses `OnEdit`.

- [ ] **Step 1: Write the failing value tests**

Create `tests/BillsMinimalApi.UnitTests/InlineEditValuesTests.cs`:

```csharp
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
        // 0.004 clears the floor before rounding and lands on 0.00 after it.
        // Re-checking after the round is what stops a zero-amount bill.
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
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter InlineEditValuesTests`
Expected: FAIL — `The name 'InlineEditValues' does not exist in the current context`.

- [ ] **Step 3: Write the parsers**

Create `BillsMinimalApi.Contracts/InlineEditValues.cs`:

```csharp
using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>
/// Reads the three values a row can be edited to, straight out of what the input
/// element sends.
/// <para>
/// In the contracts project because it is the only part of inline editing a unit
/// test can reach, and it is the part where being wrong is expensive: there is no
/// <c>EditForm</c> in front of these values, so whatever they return goes into a
/// PUT.
/// </para>
/// </summary>
public static class InlineEditValues
{
    /// <summary>
    /// The smallest amount the API will accept, from the
    /// <c>[Range(0.01, double.MaxValue)]</c> on <c>BillDto.PaymentDue</c>.
    /// Duplicated here on purpose: the alternative is letting the field commit a
    /// value the server is certain to refuse.
    /// </summary>
    public const decimal MinimumAmount = 0.01m;

    /// <summary>
    /// Reads <c>yyyy-MM-dd</c> — the only form <c>input type="date"</c> sends,
    /// whatever the browser displays.
    /// </summary>
    public static bool TryParseDate(string? raw, out DateTime value)
    {
        value = default;

        if (!DateTime.TryParseExact(
                raw,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        // Utc, not Unspecified. This travels to the API and on to a
        // `timestamp with time zone` column, and Npgsql throws on any other
        // kind — a failure that would surface as a 500 rather than as a field
        // that declines to commit.
        value = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
        return true;
    }

    /// <summary>
    /// Reads what <c>input type="number"</c> sends: a plain invariant number,
    /// dot-separated, optionally signed. No thousands separators, because the
    /// element never produces one.
    /// </summary>
    public static bool TryParseAmount(string? raw, out decimal value)
    {
        value = 0m;

        if (!decimal.TryParse(
                raw,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        if (parsed < MinimumAmount)
        {
            return false;
        }

        var rounded = decimal.Round(parsed, 2, MidpointRounding.AwayFromZero);

        // Checked again after rounding: 0.004 clears the floor and then rounds
        // to nothing, which would post a zero-amount bill the API refuses.
        if (rounded < MinimumAmount)
        {
            return false;
        }

        value = rounded;
        return true;
    }

    /// <summary>
    /// Trims the payee and refuses a blank one, matching the DTO's
    /// <c>[Required]</c>.
    /// </summary>
    public static bool TryParsePayee(string? raw, out string value)
    {
        value = raw?.Trim() ?? string.Empty;
        return value.Length > 0;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter InlineEditValuesTests`
Expected: PASS, 21 tests (the theories count per case).

- [ ] **Step 5: Add the edit envelope**

Create `bills-frontend/BillsFrontEndBlazor/Models/BillEdit.cs`:

```csharp
namespace BillsFrontEndBlazor.Models
{
    /// <summary>
    /// One committed inline edit, travelling from the row that captured it up to
    /// the page that can save it.
    /// <para>
    /// The new value arrives as an <see cref="Action{T}"/> rather than as three
    /// nullable fields and a discriminator, because the page does not need to
    /// know which field moved — it needs to apply the change, PUT the bill, and
    /// put the old value back if the server says no. One envelope replaces three
    /// parallel callbacks and three near-identical save methods.
    /// </para>
    /// </summary>
    /// <param name="Bill">The row's bill, still holding its old values.</param>
    /// <param name="Apply">Writes the new value onto a bill.</param>
    public sealed record BillEdit(Bill Bill, Action<Bill> Apply);
}
```

- [ ] **Step 6: Write the inline editor**

Create `bills-frontend/BillsFrontEndBlazor/Shared/InlineEdit.razor`:

```razor
@using System.Globalization
@using BillsMinimalApi.Contracts
@using Microsoft.AspNetCore.Components.Web

@* Idea 7. Reads as text until you click it, then becomes the right input for
   its kind. One component for all three kinds rather than three, because the
   behaviour — begin, draft, commit, cancel, restore focus — is the whole
   component and only the input tag differs. *@
@if (_editing)
{
    <input @ref="_input"
           class="editor @(_invalid ? "invalid" : null)"
           type="@InputType"
           step="@(Kind == InlineEditKind.Amount ? "0.01" : null)"
           min="@(Kind == InlineEditKind.Amount ? "0.01" : null)"
           aria-label="@Label"
           aria-invalid="@(_invalid ? "true" : null)"
           value="@_draft"
           @oninput="OnInput"
           @onkeydown="OnKeyDownAsync"
           @onfocusout="OnFocusOutAsync" />
}
else
{
    @* A button, not a span with a click handler. This is the only way to reach
       the value from a keyboard, and a span is not in the tab order and does not
       announce itself as anything you can activate. *@
    <button @ref="_reader"
            type="button"
            class="reader"
            title="Edit @Label"
            disabled="@Disabled"
            @onclick="BeginEditing">
        @Display
    </button>
}

@code {
    [Parameter, EditorRequired]
    public InlineEditKind Kind { get; set; }

    /// <summary>What the cell reads as when it is not being edited — already
    /// formatted, because only the caller knows how.</summary>
    [Parameter, EditorRequired]
    public string Display { get; set; } = string.Empty;

    /// <summary>Names the field for a screen reader and for the tooltip: "amount
    /// for Verizon", not "amount".</summary>
    [Parameter, EditorRequired]
    public string Label { get; set; } = string.Empty;

    [Parameter]
    public string? Payee { get; set; }

    [Parameter]
    public DateTime? Date { get; set; }

    /// <summary>
    /// Nullable so a caller with no amount yet — the quick-add reading in
    /// Task 12 — opens an empty field rather than one seeded with "0.00" that
    /// the user has to clear before typing.
    /// </summary>
    [Parameter]
    public decimal? Amount { get; set; }

    /// <summary>Set while the row has a write in flight, so a second edit cannot
    /// be started against a value that is about to be replaced.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback<string> PayeeCommitted { get; set; }

    [Parameter]
    public EventCallback<DateTime> DateCommitted { get; set; }

    [Parameter]
    public EventCallback<decimal> AmountCommitted { get; set; }

    private ElementReference _input;
    private ElementReference _reader;

    private bool _editing;
    private bool _invalid;
    private string? _draft;

    private bool _focusEditor;
    private bool _focusReader;

    private string InputType => Kind switch
    {
        InlineEditKind.Date => "date",
        InlineEditKind.Amount => "number",
        _ => "text",
    };

    private void BeginEditing()
    {
        // Seeded from the value rather than from Display: Display is "$89.20"
        // and "Aug 21, 2026", neither of which an input of this type accepts.
        _draft = Kind switch
        {
            InlineEditKind.Date => Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            InlineEditKind.Amount => Amount?.ToString("0.00", CultureInfo.InvariantCulture),
            _ => Payee,
        };

        _invalid = false;
        _editing = true;
        _focusEditor = true;
    }

    private void OnInput(ChangeEventArgs e)
    {
        _draft = e.Value?.ToString();

        // Clears the moment the value changes, so the red edge marks what is
        // wrong now rather than what was wrong when Enter was last pressed.
        _invalid = false;
    }

    private Task OnFocusOutAsync() => TryCommitAsync(fromKeyboard: false);

    private async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            Close(returnFocus: true);
            return;
        }

        if (e.Key == "Enter")
        {
            await TryCommitAsync(fromKeyboard: true);
        }
    }

    /// <summary>
    /// Commits the draft, or refuses it.
    /// </summary>
    /// <param name="fromKeyboard">
    /// True for Enter, false for clicking away. It decides what an invalid value
    /// does: Enter is a request to save, so the field stays open and marks
    /// itself; clicking away is a request to be somewhere else, so the edit is
    /// dropped rather than trapping focus in a field you have left.
    /// </param>
    private async Task TryCommitAsync(bool fromKeyboard)
    {
        // Escape closes the field, and removing an element can still raise
        // focusout on the way out. Without this guard that stray event would
        // commit the value Escape just discarded.
        if (!_editing)
        {
            return;
        }

        switch (Kind)
        {
            case InlineEditKind.Date
                when InlineEditValues.TryParseDate(_draft, out var date):

                Close(returnFocus: fromKeyboard);

                // Nothing changed, nothing to write. Otherwise clicking a date
                // and clicking away would spend a PUT and bump the version.
                if (date != Date)
                {
                    await DateCommitted.InvokeAsync(date);
                }

                return;

            case InlineEditKind.Amount
                when InlineEditValues.TryParseAmount(_draft, out var amount):

                Close(returnFocus: fromKeyboard);

                if (amount != Amount)
                {
                    await AmountCommitted.InvokeAsync(amount);
                }

                return;

            case InlineEditKind.Text
                when InlineEditValues.TryParsePayee(_draft, out var payee):

                Close(returnFocus: fromKeyboard);

                if (!string.Equals(payee, Payee, StringComparison.Ordinal))
                {
                    await PayeeCommitted.InvokeAsync(payee);
                }

                return;
        }

        if (fromKeyboard)
        {
            _invalid = true;
            return;
        }

        Close(returnFocus: false);
    }

    private void Close(bool returnFocus)
    {
        _editing = false;
        _invalid = false;
        _focusReader = returnFocus;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // FocusAsync is JS interop, so this belongs nowhere else: OnAfterRender
        // does not run during the prerender pass, which is the one place in a
        // ServerPrerendered circuit where IJSRuntime is unusable.
        if (_focusEditor)
        {
            _focusEditor = false;
            await _input.FocusAsync();
        }
        else if (_focusReader)
        {
            // Only after Enter or Escape. A commit by clicking away must not drag
            // focus back — the user has already chosen where it should go.
            _focusReader = false;
            await _reader.FocusAsync();
        }
    }
}
```

Create `bills-frontend/BillsFrontEndBlazor/Shared/InlineEdit.razor.css`:

```css
/* Reads as the text it replaced. The affordance is the hover underline and the
   cursor — a cell that looked like a button five times a row would turn the
   list into a toolbar. */
.reader {
    background: none;
    border: 0;
    border-radius: var(--radius);
    color: inherit;
    cursor: text;
    display: block;
    font: inherit;
    margin: 0;
    max-width: 100%;
    overflow: hidden;
    padding: .15rem .3rem;
    text-align: inherit;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.reader:hover:not(:disabled) {
    background: var(--sunken);
}

.reader:focus-visible {
    outline: var(--border-width) solid var(--accent);
    outline-offset: 1px;
}

.reader:disabled {
    cursor: default;
}

.editor {
    background: var(--sunken);
    border: var(--border-width) solid var(--accent);
    border-radius: var(--radius);
    color: var(--text);
    font: inherit;
    padding: .1rem .25rem;
    width: 100%;
}

.editor.invalid {
    border-color: var(--late);
}
```

- [ ] **Step 7: Turn three cells into editors**

In `bills-frontend/BillsFrontEndBlazor/Shared/BillGroup.razor`, replace the payee, due and amount cells — everything from `<span class="payee">` through `<span class="amount">…</span>` — with:

```razor
                <InlineEdit Kind="InlineEditKind.Text"
                            Display="@bill.PayeeName"
                            Label="@($"payee for {bill.PayeeName}")"
                            Payee="@bill.PayeeName"
                            Disabled="@BusyIds.Contains(bill.Id)"
                            PayeeCommitted="@(value => OnFieldEdited.InvokeAsync(
                                new BillEdit(bill, b => b.PayeeName = value)))" />

                <span class="due">
                    <InlineEdit Kind="InlineEditKind.Date"
                                Display="@DueDateText(bill)"
                                Label="@($"due date for {bill.PayeeName}")"
                                Date="@bill.DueDate"
                                Disabled="@BusyIds.Contains(bill.Id)"
                                DateCommitted="@(value => OnFieldEdited.InvokeAsync(
                                    new BillEdit(bill, b => b.DueDate = value)))" />

                    @if (DueRelativeText(bill, Today) is { } relative)
                    {
                        <span class="relative">@relative</span>
                    }
                </span>

                <span class="amount">
                    <InlineEdit Kind="InlineEditKind.Amount"
                                Display="@bill.PaymentDue.ToString("C")"
                                Label="@($"amount for {bill.PayeeName}")"
                                Amount="@bill.PaymentDue"
                                Disabled="@BusyIds.Contains(bill.Id)"
                                AmountCommitted="@(value => OnFieldEdited.InvokeAsync(
                                    new BillEdit(bill, b => b.PaymentDue = value)))" />
                </span>
```

In the same file, delete the Edit button from `<span class="actions">` — the whole block:

```razor
                    <button type="button" class="icon-button" title="Edit"
                            aria-label="Edit @bill.PayeeName"
                            @onclick="@(() => OnEdit.InvokeAsync(bill))">
                        <Icon Name="pencil" Size="16" />
                    </button>

```

And in its `@code` block, replace the `OnEdit` parameter:

```csharp
    [Parameter]
    public EventCallback<Bill> OnEdit { get; set; }
```

with:

```csharp
    /// <summary>Raised when a cell commits a new value. The page owns the write,
    /// because it owns the reload and the toast that follow it.</summary>
    [Parameter]
    public EventCallback<BillEdit> OnFieldEdited { get; set; }
```

- [ ] **Step 8: Let the editors own their cell padding**

In `bills-frontend/BillsFrontEndBlazor/Shared/BillGroup.razor.css`, replace the `.payee` rule:

```css
.payee {
    color: var(--text);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}
```

with:

```css
/* The payee cell is now an InlineEdit, which handles its own truncation. The
   -.3rem pulls the row back into line with the group header, which the editor's
   own padding would otherwise push it out of. */
::deep .reader {
    margin-left: -.3rem;
}

.amount ::deep .reader,
.amount ::deep .editor {
    text-align: right;
}
```

`::deep` is required: the scope attribute is stamped on this component's own markup, not on the markup `InlineEdit` renders inside it.

- [ ] **Step 9: Make the modal create-only, and say where editing went**

In `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor`, replace the whole create/edit modal — from the `@* CREATE / EDIT MODAL` comment down to and including its closing `}` (currently lines 289–357; Tasks 9 and 10 did not touch it) — with:

```razor
@* CREATE MODAL. Editing happens in the rows now, so this is the one job the
   modal still has: a bill that does not exist yet has no row to click.
   "modal fade show d-block" plus a backdrop colour is what makes a Bootstrap
   modal work without bootstrap.bundle.js. *@
@if (_isCreating)
{
    <div class="modal fade show d-block" tabindex="-1" role="dialog"
         style="background-color: rgba(0,0,0,0.5);">
        <div class="modal-dialog modal-dialog-centered">
            <div class="modal-content">

                <div class="modal-header">
                    <h5 class="modal-title">
                        <Icon Name="plus" Size="18" Class="me-2" />
                        New bill
                    </h5>
                    <button type="button" class="btn-close" aria-label="Close" @onclick="CloseForm"></button>
                </div>

                <EditForm Model="_formBill" OnValidSubmit="SaveFormAsync">
                    <DataAnnotationsValidator />

                    <div class="modal-body">
                        <div class="mb-3">
                            <label class="form-label" for="form-payee">Payee</label>
                            <input id="form-payee" class="form-control" @bind="_formBill.PayeeName" />
                            <ValidationMessage For="@(() => _formBill.PayeeName)" class="text-danger small" />
                        </div>

                        <div class="mb-3">
                            <label class="form-label" for="form-due">Due date</label>
                            <input id="form-due" type="date" class="form-control" @bind="_formBill.DueDate" />
                            <ValidationMessage For="@(() => _formBill.DueDate)" class="text-danger small" />
                        </div>

                        <div class="mb-3">
                            <label class="form-label" for="form-amount">Amount</label>
                            <div class="input-group">
                                <span class="input-group-text">$</span>
                                <input id="form-amount" type="number" step="0.01" class="form-control"
                                       @bind="_formBill.PaymentDue" />
                            </div>
                            <ValidationMessage For="@(() => _formBill.PaymentDue)" class="text-danger small" />
                        </div>

                        @* Kept: a bill can be entered after it was settled, and
                           the alternative is creating it and then toggling it. *@
                        <div class="form-check">
                            <input class="form-check-input" type="checkbox" id="form-paid" @bind="_formBill.Paid" />
                            <label class="form-check-label" for="form-paid">Already paid</label>
                        </div>
                    </div>

                    <div class="modal-footer">
                        <button type="button" class="btn btn-secondary" @onclick="CloseForm" disabled="@_isSaving">
                            Cancel
                        </button>
                        <button type="submit" class="btn btn-primary" disabled="@_isSaving">
                            @if (_isSaving)
                            {
                                <span class="spinner-border spinner-border-sm me-1" role="status"></span>
                            }
                            Add bill
                        </button>
                    </div>
                </EditForm>

            </div>
        </div>
    </div>
}
```

Then add the footnote inside `<div class="bills">`, immediately after the `@if (SelectedCount > 0)` block added in Task 10 and before the closing `</div>`:

```razor
    @* Copy verbatim from the design. It earns its place by answering the
       question the redesign creates — "where did the edit button go?" — at the
       moment someone looks for it. *@
    <p class="footnote">
        Dates and amounts are editable where they sit — click one. The modal
        survives only for a bill you are creating from scratch.
        <button type="button" class="link" @onclick="OpenCreateModal">Open the create form</button>
    </p>
```

And add to `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor.css`:

```css
.footnote {
    color: var(--muted);
    font-size: .78rem;
    margin: 0;
    max-width: 62ch;
    text-wrap: pretty;
}

.link {
    background: none;
    border: 0;
    color: var(--accent-text);
    cursor: pointer;
    font: inherit;
    padding: 0;
    text-decoration: underline;
    text-underline-offset: 3px;
}
```

- [ ] **Step 10: Collapse the form mode and save the edits**

In `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor.cs`, replace the `FormMode` enum and the `_formMode` field:

```csharp
        private enum FormMode
        {
            None,
            Create,
            Edit,
        }

        private FormMode _formMode = FormMode.None;
```

with:

```csharp
        /// <summary>
        /// Whether the create modal is open. A bool now that the same block no
        /// longer has to be two forms — editing happens in the rows.
        /// </summary>
        private bool _isCreating;
```

Replace `OpenCreateModal`, `OpenEditModal` and `CloseForm`:

```csharp
        private void OpenCreateModal()
        {
            _formBill = new Bill { DueDate = DateTime.Today };
            _formMode = FormMode.Create;
        }

        private void OpenEditModal(Bill bill)
        {
            // A copy, not the page's instance: cancelling the form must not leave
            // half-typed values rendered in the row behind it.
            _formBill = new Bill
            {
                Id = bill.Id,
                PayeeName = bill.PayeeName,
                DueDate = bill.DueDate,
                PaymentDue = bill.PaymentDue,
                Paid = bill.Paid,
                Version = bill.Version,
            };

            _formMode = FormMode.Edit;
        }

        private void CloseForm()
        {
            _formMode = FormMode.None;
            _isSaving = false;
        }
```

with:

```csharp
        private void OpenCreateModal()
        {
            _formBill = new Bill { DueDate = DateTime.Today };
            _isCreating = true;
        }

        private void CloseForm()
        {
            _isCreating = false;
            _isSaving = false;
        }
```

Replace `SaveFormAsync`, which no longer has two paths:

```csharp
        private async Task SaveFormAsync()
        {
            if (_isSaving)
            {
                return;
            }

            _isSaving = true;

            try
            {
                var creating = _formMode == FormMode.Create;

                var result = creating
                    ? await BillService.CreateBillAsync(_formBill)
                    : await BillService.UpdateBillAsync(_formBill);

                if (!result.Success)
                {
                    Toasts.ShowError(result.ToMessage(creating ? "create" : "update"));

                    // A 409 means someone else won the race and our copy is stale,
                    // and a 404 means the row is gone — in both cases the list on
                    // screen is wrong, so refresh it before the retry. The form
                    // stays open with the user's values intact.
                    if (result.IsConflict || result.IsNotFound)
                    {
                        AfterWrite();
                    }

                    return;
                }

                Toasts.ShowSuccess(creating ? "Bill created" : "Bill updated");
                CloseForm();
                AfterWrite();
            }
            finally
            {
                _isSaving = false;
            }
        }
```

with:

```csharp
        private async Task SaveFormAsync()
        {
            if (_isSaving)
            {
                return;
            }

            _isSaving = true;

            try
            {
                var result = await BillService.CreateBillAsync(_formBill);

                if (!result.Success)
                {
                    // The form stays open with the values intact, so a rejection
                    // costs a retry rather than the whole entry.
                    Toasts.ShowError(result.ToMessage("create"));
                    return;
                }

                Toasts.ShowSuccess("Bill created");
                CloseForm();
                AfterWrite();
            }
            finally
            {
                _isSaving = false;
            }
        }
```

Add `SaveEditAsync` immediately after `TogglePaidAsync`:

```csharp
        /// <summary>
        /// Saves one inline edit. The same optimistic shape as
        /// <see cref="TogglePaidAsync"/>: apply it, write it, put it back if the
        /// server refuses.
        /// </summary>
        private async Task SaveEditAsync(BillEdit edit)
        {
            var bill = edit.Bill;

            // Guards a second edit landing on a bill that already has a write in
            // flight — the version in hand would be one behind by the time it
            // arrived, and the second write would 409 against our own first one.
            if (!_busyIds.Add(bill.Id))
            {
                return;
            }

            // The three editable fields, kept so a refusal can be undone. Cheaper
            // and more honest than reloading: a failed write should leave the page
            // exactly as it was, not as the server last saw it.
            var payee = bill.PayeeName;
            var dueDate = bill.DueDate;
            var amount = bill.PaymentDue;

            edit.Apply(bill);

            try
            {
                var result = await BillService.UpdateBillAsync(bill);

                if (!result.Success)
                {
                    bill.PayeeName = payee;
                    bill.DueDate = dueDate;
                    bill.PaymentDue = amount;

                    Toasts.ShowError(result.ToMessage("update"));

                    if (result.IsConflict || result.IsNotFound)
                    {
                        AfterWrite();
                    }

                    return;
                }

                Toasts.ShowSuccess("Bill updated");

                // A due-date edit can move the row to another group, and every
                // write bumps Version — so the list has to come back either way.
                AfterWrite();
            }
            finally
            {
                _busyIds.Remove(bill.Id);
            }
        }
```

Finally, in `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor`, point the group at the new callback — replace:

```razor
                           OnEdit="@OpenEditModal"
```

with:

```razor
                           OnFieldEdited="@SaveEditAsync"
```

- [ ] **Step 11: Run the whole suite**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln`
Expected: PASS. Any surviving reference to `FormMode`, `_formMode` or `OpenEditModal` fails the Blazor build here.

- [ ] **Step 12: Verify it by eye**

Run the app and go to Bills:
1. Click a payee, a date and an amount in turn — each becomes the right kind of input, focused, with the current value in it.
2. Change an amount and click away — it saves, a toast confirms, and the row's total and its group sum both move.
3. Change a due date to next month — the row leaves its group and lands in the right one.
4. Press Escape mid-edit — the old value is back and focus is on the cell you started from.
5. Type `0` into an amount and press Enter — the field stays open with a red edge, and nothing is written.
6. Type `0` into an amount and click away — the edit is dropped, no toast, no write.
7. Click a cell and click away without changing anything — no toast and no request in the network tab.
8. There is no pencil on any row; "Add bill" and the footnote's "Open the create form" both open the create modal, and its title reads "New bill".

- [ ] **Step 13: Commit**

```bash
git add -A bills-frontend/BillsFrontEndBlazor BillsMinimalApi.Contracts/InlineEditValues.cs tests/BillsMinimalApi.UnitTests/InlineEditValuesTests.cs
git commit -m "Edit a bill where it sits, and keep the modal for new ones"
```

---

### Task 12: Add a bill in words

Idea 8, and the reason Tasks 1 and 2 exist. Type `Verizon 89.20 fri`, see what the server made of it, correct the piece it got wrong, add it.

**One thing this removes:** the "Add bill" button in the Bills page header, added in Task 9. Two buttons reading "Add bill" on one page — one committing a parse, one opening a modal — is a coin toss for the user. The quick-add row keeps the name because it is the primary way to add a bill now; the modal keeps the footnote link Task 11 gave it, which is exactly what that footnote promises.

**The reading is correctable through `InlineEdit`.** The prototype renders three static chips. The handoff is explicit that the pieces come back uncommitted "so the client can render the reading for the user to confirm/edit before POSTing the real bill" — and Task 11 already built the component for editing a payee, a date and an amount in place. The chips are `InlineEdit` instances. A misread date costs one click, not a retyped sentence.

**Files:**
- Create: `BillsMinimalApi.Contracts/ParsedBillReading.cs`
- Create: `tests/BillsMinimalApi.UnitTests/ParsedBillReadingTests.cs`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/QuickAddBill.razor`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/QuickAddBill.razor.css`
- Modify: `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor` (the header loses a button, the page gains the box)
- Modify: `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor.cs` (`AddQuickBillAsync`)
- Modify: `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor.css` (`.head-actions` goes)

**Interfaces:**
- Consumes: `ParsedBill`, `ParseConfidence` (Task 1), `BillService.ParseBillAsync(string, CancellationToken)` (Task 2), `InlineEditValues.MinimumAmount` and `<InlineEdit>` (Task 11), `BillService.CreateBillAsync` (existing, line 214).
- Produces:
  - `ParsedBillReading.IsComplete(ParsedBill?) -> bool`
  - `ParsedBillReading.PayeeText(ParsedBill?) -> string`
  - `ParsedBillReading.AmountText(ParsedBill?, IFormatProvider?) -> string`
  - `ParsedBillReading.DueText(ParsedBill?, IFormatProvider?) -> string`
  - `<QuickAddBill OnAdd Busy />`, where `OnAdd` is `Func<Bill, Task<bool>>`

- [ ] **Step 1: Write the failing reading tests**

Create `tests/BillsMinimalApi.UnitTests/ParsedBillReadingTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter ParsedBillReadingTests`
Expected: FAIL — `The name 'ParsedBillReading' does not exist in the current context`.

- [ ] **Step 3: Write the reading**

Create `BillsMinimalApi.Contracts/ParsedBillReading.cs`:

```csharp
using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>
/// Reads a <see cref="ParsedBill"/> back to the person who typed it: three
/// chips, and whether they add up to something worth posting.
/// <para>
/// Separate from <see cref="ParseConfidence"/> on purpose. Confidence is the
/// server's report on its own parse and never changes; this is asked again every
/// time the user corrects a chip, and a low-confidence parse that has since been
/// filled in by hand is perfectly postable.
/// </para>
/// </summary>
public static class ParsedBillReading
{
    /// <summary>Shown in place of a piece the parser did not find. Written as an
    /// instruction because the chip is clickable — it is the fix, not just the
    /// complaint.</summary>
    public const string MissingPayee = "add a payee";

    public const string MissingAmount = "add an amount";

    public const string MissingDate = "add a date";

    /// <summary>
    /// Whether this reading would survive <c>POST /restapi/BillDtos</c>. The
    /// amount floor is <see cref="InlineEditValues.MinimumAmount"/> rather than
    /// "not null", because the parser takes the first number in the line and zero
    /// is a number.
    /// </summary>
    public static bool IsComplete(ParsedBill? reading) =>
        reading is not null
        && !string.IsNullOrWhiteSpace(reading.Payee)
        && reading.Amount >= InlineEditValues.MinimumAmount
        && reading.DueDate is not null;

    public static string PayeeText(ParsedBill? reading)
    {
        var payee = reading?.Payee?.Trim();
        return string.IsNullOrEmpty(payee) ? MissingPayee : payee;
    }

    public static string AmountText(ParsedBill? reading, IFormatProvider? formats = null) =>
        reading?.Amount is { } amount
            ? amount.ToString("C", formats ?? CultureInfo.CurrentCulture)
            : MissingAmount;

    public static string DueText(ParsedBill? reading, IFormatProvider? formats = null) =>
        reading?.DueDate is { } due
            ? due.ToString("MMM d, yyyy", formats ?? CultureInfo.CurrentCulture)
            : MissingDate;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter ParsedBillReadingTests`
Expected: PASS, 14 tests.

- [ ] **Step 5: Write the quick-add box**

Create `bills-frontend/BillsFrontEndBlazor/Shared/QuickAddBill.razor`:

```razor
@using BillsFrontEndBlazor.Models
@using BillsMinimalApi.Contracts
@using Microsoft.AspNetCore.Components.Web
@implements IDisposable
@inject BillsFrontEndBlazor.Services.BillService BillService

@* Idea 8. The line goes to the server, the server says what it made of it, and
   the three chips are editable — so a misread date costs one click rather than
   a retyped sentence. *@
<section class="quick-add">

    <div class="entry">
        @* Curly quotes, verbatim from the design. *@
        <input class="text"
               aria-label="Add a bill in words"
               placeholder="Add a bill in words — try “Verizon 89.20 fri”"
               value="@_text"
               @oninput="OnInputAsync"
               @onkeydown="OnKeyDownAsync" />

        <button type="button" class="primary" disabled="@(!CanAdd)" @onclick="CommitAsync">
            <Icon Name="plus" Size="16" Class="me-1" />
            Add bill
        </button>
    </div>

    @if (_reading is not null)
    {
        <div class="reading">
            <span class="label">Reads as</span>

            <InlineEdit Kind="InlineEditKind.Text"
                        Display="@ParsedBillReading.PayeeText(_reading)"
                        Label="payee"
                        Payee="@_reading.Payee"
                        PayeeCommitted="SetPayee" />

            <InlineEdit Kind="InlineEditKind.Amount"
                        Display="@ParsedBillReading.AmountText(_reading)"
                        Label="amount"
                        Amount="@_reading.Amount"
                        AmountCommitted="SetAmount" />

            <InlineEdit Kind="InlineEditKind.Date"
                        Display="@ParsedBillReading.DueText(_reading)"
                        Label="due date"
                        Date="@_reading.DueDate"
                        DateCommitted="SetDueDate" />
        </div>
    }

</section>

@code {
    /// <summary>
    /// Saves the bill and reports whether it stuck.
    /// <para>
    /// A <see cref="Func{T, TResult}"/> rather than an <c>EventCallback</c>
    /// because this component needs the answer: the box empties on a save and
    /// keeps the typed line on a refusal, and an <c>EventCallback</c> returns
    /// nothing to decide that with.
    /// </para>
    /// </summary>
    [Parameter, EditorRequired]
    public Func<Bill, Task<bool>> OnAdd { get; set; } = _ => Task.FromResult(false);

    /// <summary>Set while the page has another write in flight.</summary>
    [Parameter]
    public bool Busy { get; set; }

    /// <summary>
    /// Longer than the payee search's 300ms: that filters a list already on
    /// screen, this spends a round trip reading a sentence that is still being
    /// written.
    /// </summary>
    private static readonly TimeSpan ParseDebounce = TimeSpan.FromMilliseconds(400);

    private string _text = string.Empty;
    private ParsedBill? _reading;
    private bool _parsing;
    private bool _committing;
    private CancellationTokenSource? _parseCts;

    /// <summary>
    /// <c>_parsing</c> is in here so the button cannot post the previous
    /// sentence's reading while the current one is still being read.
    /// </summary>
    private bool CanAdd =>
        !Busy && !_committing && !_parsing && ParsedBillReading.IsComplete(_reading);

    private async Task OnInputAsync(ChangeEventArgs e)
    {
        _text = e.Value?.ToString() ?? string.Empty;

        _parseCts?.Cancel();
        _parseCts?.Dispose();
        _parseCts = null;

        if (string.IsNullOrWhiteSpace(_text))
        {
            _parsing = false;
            _reading = null;
            return;
        }

        var cts = new CancellationTokenSource();
        _parseCts = cts;
        _parsing = true;

        try
        {
            await Task.Delay(ParseDebounce, cts.Token);

            var reading = await BillService.ParseBillAsync(_text, cts.Token);

            // A newer keystroke cancelled us while the request was in flight.
            // Its answer describes what is on screen; ours describes text that
            // has already been typed over.
            if (cts.IsCancellationRequested)
            {
                return;
            }

            // Null means the server could not be asked. Clearing is the honest
            // reading — the alternative is leaving a stale one on screen beside
            // a live "Add bill" button.
            _reading = reading;
            _parsing = false;

            StateHasChanged();
        }
        catch (OperationCanceledException)
        {
            // Superseded. The keystroke that cancelled this owns the state now,
            // including _parsing, which it has already set back to true.
        }
    }

    private async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            Reset();
            return;
        }

        if (e.Key == "Enter" && CanAdd)
        {
            await CommitAsync();
        }
    }

    private void SetPayee(string value)
    {
        if (_reading is not null)
        {
            _reading.Payee = value;
        }
    }

    private void SetAmount(decimal value)
    {
        if (_reading is not null)
        {
            _reading.Amount = value;
        }
    }

    private void SetDueDate(DateTime value)
    {
        if (_reading is not null)
        {
            _reading.DueDate = value;
        }
    }

    private async Task CommitAsync()
    {
        if (!CanAdd || _reading is null)
        {
            return;
        }

        _committing = true;

        try
        {
            var bill = new Bill
            {
                PayeeName = _reading.Payee!.Trim(),

                // Stamped again on the way out. This date has crossed JSON twice
                // since the parser set it, and Npgsql refuses a DateTime that is
                // not UTC.
                DueDate = DateTime.SpecifyKind(_reading.DueDate!.Value.Date, DateTimeKind.Utc),

                PaymentDue = _reading.Amount!.Value,
                Paid = false,
            };

            if (await OnAdd(bill))
            {
                Reset();
            }
        }
        finally
        {
            _committing = false;
        }
    }

    private void Reset()
    {
        _parseCts?.Cancel();
        _parseCts?.Dispose();
        _parseCts = null;

        _text = string.Empty;
        _reading = null;
        _parsing = false;
    }

    public void Dispose()
    {
        _parseCts?.Cancel();
        _parseCts?.Dispose();
    }
}
```

Create `bills-frontend/BillsFrontEndBlazor/Shared/QuickAddBill.razor.css`:

```css
.quick-add {
    display: flex;
    flex-direction: column;
    gap: .6rem;
}

.entry {
    display: flex;
    gap: .5rem;
}

.text {
    background: var(--surface);
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius);
    color: var(--text);
    flex: 1;
    font: inherit;
    min-height: 2.6rem;
    padding: .55rem .875rem;
}

.text::placeholder {
    color: var(--faint);
}

.text:focus-visible {
    border-color: var(--accent);
    outline: none;
}

/* Outline, not a flood fill: the accent is a line colour in this palette. */
.primary {
    align-items: center;
    background: none;
    border: var(--border-width) solid var(--accent);
    border-radius: var(--radius);
    color: var(--accent-text);
    cursor: pointer;
    display: inline-flex;
    padding: .4rem .9rem;
}

.primary:disabled {
    border-color: var(--border);
    color: var(--muted);
    cursor: default;
}

.reading {
    align-items: center;
    color: var(--muted);
    display: flex;
    flex-wrap: wrap;
    font-size: .78rem;
    gap: .5rem;
}

/* The three pieces are InlineEdit instances, so a misreading is one click from
   fixed. ::deep because the scope attribute lands on this component's own
   markup, not on what InlineEdit renders inside it — and these rules carry more
   weight than InlineEdit's own, which is what lets a plain reader become a
   chip here without changing it everywhere else. */
.reading ::deep .reader,
.reading ::deep .editor {
    border-radius: 6px;
    color: var(--accent-text);
    font-size: .78rem;
    padding: .18rem .55rem;
}

.reading ::deep .reader {
    border: var(--border-width) solid var(--accent);
}

.reading ::deep .editor {
    max-width: 11rem;
}
```

- [ ] **Step 6: Put the box on the page and take the duplicate button off**

In `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor`, replace the header's action block:

```razor
        <div class="head-actions">
            <button type="button" class="ghost" @onclick="LoadBillsAsync" disabled="@_isLoading">
                <Icon Name="arrows-clockwise" Size="16" Class="me-1" />
                Refresh
            </button>

            <button type="button" class="primary" @onclick="OpenCreateModal">
                <Icon Name="plus" Size="16" Class="me-1" />
                Add bill
            </button>
        </div>
```

with:

```razor
        @* "Add bill" now lives on the quick-add row below, where it commits a
           reading. The modal is reached from the footnote at the foot of the
           page, which is what that footnote is for. *@
        <button type="button" class="ghost" @onclick="LoadBillsAsync" disabled="@_isLoading">
            <Icon Name="arrows-clockwise" Size="16" Class="me-1" />
            Refresh
        </button>
```

Then insert the box immediately before `<div class="controls">`:

```razor
    <QuickAddBill OnAdd="AddQuickBillAsync" Busy="@_isBulkWriting" />

```

And in `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor.css`, delete the now-unused rule:

```css
.head-actions {
    display: flex;
    gap: .5rem;
}
```

- [ ] **Step 7: Save what the box hands over**

In `bills-frontend/BillsFrontEndBlazor/Pages/Bills.razor.cs`, add immediately after `SaveFormAsync`:

```csharp
        /// <summary>
        /// Saves a bill built from the quick-add reading, and reports back so the
        /// box knows whether to empty itself.
        /// </summary>
        private async Task<bool> AddQuickBillAsync(Bill bill)
        {
            var result = await BillService.CreateBillAsync(bill);

            if (!result.Success)
            {
                // The typed line stays where it is. A refusal should cost a
                // retry, not the sentence.
                Toasts.ShowError(result.ToMessage("create"));
                return false;
            }

            // Named rather than generic: the box is emptying, so the toast is the
            // only confirmation of what went in.
            Toasts.ShowSuccess($"Added {bill.PayeeName}");
            AfterWrite();
            return true;
        }
```

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln`
Expected: PASS.

- [ ] **Step 9: Verify it by eye**

Run the app and go to Bills:
1. Type `Verizon 89.20 fri`. After a beat, "Reads as" appears with three chips: `Verizon`, `$89.20`, and the coming Friday's date.
2. There is exactly one "Add bill" button on the page, and it is on the quick-add row. Press it — the bill appears in the right group, a toast names it, and the box empties.
3. Type `Verizon` alone. The payee chip fills, the other two read "add an amount" and "add a date", and "Add bill" is disabled.
4. Click "add a date" — a date field opens, focused and empty. Pick one; the chip fills, and once the amount is filled too "Add bill" comes alive.
5. Type quickly across a whole phrase and watch the network tab: one `/parse` request lands after you stop, not one per keystroke.
6. Press Escape while typing — the box empties and the reading goes with it.
7. The footnote at the foot of the page still opens the create modal.

- [ ] **Step 10: Commit**

```bash
git add -A bills-frontend/BillsFrontEndBlazor BillsMinimalApi.Contracts/ParsedBillReading.cs tests/BillsMinimalApi.UnitTests/ParsedBillReadingTests.cs
git commit -m "Add a bill by typing a line and correcting the reading"
```

---

### Task 13: Rank payees by what they cost you

Idea 9. The Reports page today lists payees in a sortable table of four
columns. The redesign ranks them by what is still outstanding and adds the one
thing a flat ranking cannot show: the running share of the total, so the reader
can see where the debt stops being concentrated.

The framing sentence — "Three payees account for 62% of everything you owe." —
is the point of the section, so it is computed rather than decorative: the
count is however many payees it takes to cross 60% of the outstanding total.

Two notes on what this task does *not* do:

- **The column sorting goes.** Today's payee table sorts on click through
  `PayeeSortColumn` / `SortPayeesBy` / `PayeeSorted` / `PayeeSortCaret` in
  `Reports.razor.cs`. A Pareto table has exactly one meaningful order — a
  running cumulative share is nonsense in any other — so the sort is removed
  rather than re-skinned. Those four members die with the old markup in Task 14.
- **Fully-paid payees drop out.** `PayeeTotals` covers every payee in the
  range, including ones with nothing outstanding. They contribute nothing to a
  "who you owe" ranking and would pad the table with a column of zeroes and a
  flat 100% cumulative, so `Build` filters them out.

**Files:**
- Create: `BillsMinimalApi.Contracts/NumberWords.cs`
- Create: `BillsMinimalApi.Contracts/ParetoRows.cs`
- Create: `tests/BillsMinimalApi.UnitTests/ParetoRowTests.cs`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/PayeePareto.razor`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/PayeePareto.razor.css`

**Interfaces:**
- Consumes: `BillSummary.Payees` — `List<PayeeTotals>`, where
  `PayeeTotals` is a top-level type in `BillsMinimalApi.Contracts` with
  `string Payee`, `int Bills`, `decimal Billed`, `decimal Paid`, and the
  derived `decimal Outstanding => Billed - Paid`. Already ordered by
  outstanding descending by `BillSummaryBuilder.PayeesAsync`, but this task
  re-orders anyway rather than trusting the caller.
- Produces:
  - `NumberWords.Spell(int count) -> string` — "One" … "Twenty" capitalised,
    digits above twenty. Task 14 uses it for the size-band sentence.
  - `sealed record ParetoRow(string Payee, int Bills, decimal Outstanding, double SharePercent, double CumulativePercent)`
  - `ParetoRows.Build(IEnumerable<PayeeTotals>? payees) -> List<ParetoRow>`
  - `ParetoRows.PayeesToReach(IReadOnlyList<ParetoRow> rows, double percent) -> int`
  - `ParetoRows.Headline(IReadOnlyList<ParetoRow> rows, double threshold = ParetoRows.HeadlineThreshold) -> string?`
  - `ParetoRows.HeadlineThreshold` — `const double` = `60d`
  - `<PayeePareto Payees="@Summary.Payees" />` — the component Task 14 drops
    into `Pages/Reports.razor`.

- [ ] **Step 1: Write the failing tests**

Create `tests/BillsMinimalApi.UnitTests/ParetoRowTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter ParetoRowTests`

Expected: FAIL to compile — `The name 'ParetoRows' does not exist in the current context` and `The type or namespace name 'ParetoRow' could not be found`.

- [ ] **Step 3: Write the number speller**

Create `BillsMinimalApi.Contracts/NumberWords.cs`:

```csharp
using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>
/// Small counts written as words, for sentences that open on one.
/// <para>
/// "Three payees account for…" reads as prose; "3 payees account for…" reads
/// as a log line. Above twenty the word is longer than the number and stops
/// helping, so the rule flips — which is the convention most style guides
/// land on and, more to the point, the one the design's own copy follows.
/// </para>
/// </summary>
public static class NumberWords
{
    /// <summary>The last count that gets a word rather than digits.</summary>
    public const int LargestSpelled = 20;

    private static readonly string[] Words =
    {
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven",
        "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen",
        "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen", "Twenty",
    };

    /// <summary>
    /// Capitalised, because every caller uses this at the start of a sentence.
    /// A caller needing it mid-sentence would lower-case the first letter
    /// itself rather than this growing a second overload nobody has asked for.
    /// </summary>
    public static string Spell(int count) =>
        count is >= 0 and <= LargestSpelled
            ? Words[count]
            : count.ToString(CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Write the cumulative-share arithmetic**

Create `BillsMinimalApi.Contracts/ParetoRows.cs`:

```csharp
using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>
/// One payee's place in the ranking: what they are owed, what share of the
/// total that is, and what share the ranking has accounted for by this row.
/// </summary>
/// <param name="SharePercent">This payee alone, 0–100.</param>
/// <param name="CumulativePercent">
/// This payee and everyone above them, 0–100. The last row is 100 by
/// construction, since the denominator is the sum of the rows themselves.
/// </param>
public sealed record ParetoRow(
    string Payee,
    int Bills,
    decimal Outstanding,
    double SharePercent,
    double CumulativePercent);

/// <summary>
/// Turns per-payee totals into a Pareto ranking — biggest debt first, with a
/// running share of the whole.
/// <para>
/// Here rather than in the component because it is arithmetic with edge cases
/// (an empty range, a fully-paid range, ties) and those are worth testing
/// without a renderer in the way.
/// </para>
/// </summary>
public static class ParetoRows
{
    /// <summary>
    /// The share the headline sentence counts payees up to. Sixty percent is
    /// the point where "a few payees are most of the problem" stops being a
    /// fair reading of the data, so it is where the sentence stops counting.
    /// </summary>
    public const double HeadlineThreshold = 60d;

    /// <summary>
    /// Ranks the payees who are still owed something.
    /// <para>
    /// The total is the sum of the rows that survive the filter, not
    /// <see cref="BillSummary.OutstandingAmount"/>. They are the same number,
    /// but deriving it here is what makes the last row exactly 100% —
    /// borrowing a separately-computed total would leave the column ending on
    /// 99.8% whenever the two rounded differently.
    /// </para>
    /// </summary>
    public static List<ParetoRow> Build(IEnumerable<PayeeTotals>? payees)
    {
        var owing = (payees ?? Enumerable.Empty<PayeeTotals>())
            .Where(p => p.Outstanding > 0m)
            .OrderByDescending(p => p.Outstanding)
            .ThenBy(p => p.Payee, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = owing.Sum(p => p.Outstanding);

        if (total <= 0m)
        {
            return new List<ParetoRow>();
        }

        var rows = new List<ParetoRow>(owing.Count);
        var running = 0m;

        foreach (var payee in owing)
        {
            running += payee.Outstanding;

            rows.Add(new ParetoRow(
                Payee: payee.Payee,
                Bills: payee.Bills,
                Outstanding: payee.Outstanding,
                SharePercent: (double)(payee.Outstanding / total) * 100d,
                CumulativePercent: (double)(running / total) * 100d));
        }

        return rows;
    }

    /// <summary>
    /// How many payees from the top it takes to account for
    /// <paramref name="percent"/> of the total.
    /// </summary>
    /// <returns>
    /// Zero for an empty ranking. Every row if the threshold is never crossed
    /// — which only happens for a threshold above 100, but returning the whole
    /// count is the honest answer either way: that is how many payees it took.
    /// </returns>
    public static int PayeesToReach(IReadOnlyList<ParetoRow> rows, double percent)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].CumulativePercent >= percent)
            {
                return i + 1;
            }
        }

        return rows.Count;
    }

    /// <summary>
    /// The section's framing sentence, or null when there is nothing to frame.
    /// </summary>
    public static string? Headline(IReadOnlyList<ParetoRow> rows, double threshold = HeadlineThreshold)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var count = PayeesToReach(rows, threshold);
        var share = rows[count - 1].CumulativePercent;

        // Invariant on the percent for the same reason the widths are: this is
        // a whole number by the time it is formatted, and a culture that
        // groups digits differently would not change it — but pinning it keeps
        // the sentence identical on every machine that renders it.
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} {2} for {3:0}% of everything you owe.",
            NumberWords.Spell(count),
            count == 1 ? "payee" : "payees",
            count == 1 ? "accounts" : "account",
            share);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter ParetoRowTests`

Expected: PASS — 24 test cases green (16 facts plus 8 theory cases). If
`Headline_matches_the_designed_sentence` fails
with "Three payees account for 62.0%…", the format string lost its `0`
specifier: `{3:0}` rounds to a whole number, `{3}` does not.

- [ ] **Step 6: Build the ranking component**

Create `bills-frontend/BillsFrontEndBlazor/Shared/PayeePareto.razor`:

```razor
@using System.Globalization

@* Idea 9: payees ranked by what is still owed, with the running share of the
   total.

   The bar is two layers rather than two bars — the cumulative share behind,
   this payee's own share in front — so a single 8px strip answers both
   questions a Pareto chart exists to answer: how much of the debt has been
   accounted for by this row, and how much of it is this one payee. *@

<section class="pareto">

    <h2 class="title">Who you owe</h2>
    <p class="subtitle">Ranked by outstanding, with the running share of the total.</p>

    @if (_headline is not null)
    {
        <p class="headline">@_headline</p>
    }

    @if (_rows.Count == 0)
    {
        <p class="empty">Nothing outstanding — every bill in this range is paid.</p>
    }
    else
    {
        <table class="ranks">
            <thead>
                <tr>
                    <th scope="col">Payee</th>
                    <th scope="col" class="num">Outstanding</th>
                    <th scope="col">Running share</th>
                    <th scope="col" class="num">Cum. %</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var row in _rows)
                {
                    <tr>
                        <th scope="row" class="payee" title="@row.Payee">@row.Payee</th>
                        <td class="num">@row.Outstanding.ToString("C")</td>
                        <td>
                            <span class="bar"
                                  role="img"
                                  aria-label="@AriaFor(row)">
                                <span class="cum" style="width: @Grow(row.CumulativePercent)%"></span>
                                <span class="own" style="width: @Grow(row.SharePercent)%"></span>
                            </span>
                        </td>
                        <td class="num">@row.CumulativePercent.ToString("0")%</td>
                    </tr>
                }
            </tbody>
        </table>
    }

</section>

@code {
    [Parameter, EditorRequired]
    public List<PayeeTotals> Payees { get; set; } = new();

    private List<ParetoRow> _rows = new();

    private string? _headline;

    protected override void OnParametersSet()
    {
        _rows = ParetoRows.Build(Payees);
        _headline = ParetoRows.Headline(_rows);
    }

    // The bar carries information no text nearby repeats — the payee's own
    // share — so it gets a label rather than aria-hidden.
    private static string AriaFor(ParetoRow row) =>
        $"{row.SharePercent:0}% of what you owe, {row.CumulativePercent:0}% running";

    // Invariant, because this ends up inside a style attribute. A culture that
    // writes 12,5 would produce CSS the browser silently drops.
    private static string Grow(double percent) =>
        percent.ToString("0.####", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 7: Style it**

Create `bills-frontend/BillsFrontEndBlazor/Shared/PayeePareto.razor.css`:

```css
.pareto {
    background: var(--surface);
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius-lg);
    padding: 1.25rem 1.5rem;
}

.title {
    color: var(--text);
    font-size: 1rem;
    font-weight: 600;
    margin: 0;
}

.subtitle,
.empty {
    color: var(--muted);
    font-size: .85rem;
    margin: .15rem 0 0;
}

/* The one sentence the section exists to deliver, so it gets the accent. */
.headline {
    color: var(--accent-text);
    font-size: .9rem;
    margin: .9rem 0 0;
}

.ranks {
    border-collapse: collapse;
    margin-top: 1rem;
    table-layout: fixed;
    width: 100%;
}

.ranks th,
.ranks td {
    padding: .55rem 0;
    text-align: left;
}

.ranks thead th {
    border-bottom: var(--border-width) solid var(--border);
    color: var(--muted);
    font-size: .69rem;
    font-weight: 500;
    letter-spacing: .07em;
    padding-bottom: .4rem;
    text-transform: uppercase;
}

.ranks tbody tr {
    border-bottom: var(--border-width) solid var(--border);
}

.ranks tbody tr:last-child {
    border-bottom: 0;
}

/* Payee takes what is left; the other three are fixed so the bars line up
   down the column regardless of how long the names are. */
.ranks th:nth-child(2),
.ranks td:nth-child(2) { width: 7rem; }

.ranks th:nth-child(3),
.ranks td:nth-child(3) { width: 40%; }

.ranks th:nth-child(4),
.ranks td:nth-child(4) { width: 4rem; }

.payee {
    color: var(--text);
    font-weight: 500;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.num {
    color: var(--text);
    font-variant-numeric: tabular-nums;
    text-align: right;
}

.ranks tbody .num:last-child {
    color: var(--muted);
}

/* Padding between the bar and the numbers on either side of it. */
.ranks td:nth-child(3) {
    padding-left: .9rem;
    padding-right: .9rem;
}

.bar {
    background: var(--sunken);
    border-radius: 4px;
    display: block;
    height: 8px;
    overflow: hidden;
    position: relative;
}

.cum,
.own {
    bottom: 0;
    left: 0;
    position: absolute;
    top: 0;
}

/* The running total is a token-derived tint of the accent rather than a
   second hue: the accent is an outline colour in Nocturne, and mixing it into
   the sunken background keeps this inside the palette without inventing one. */
.cum { background: color-mix(in srgb, var(--accent) 28%, var(--sunken)); }

.own { background: var(--accent); }
```

- [ ] **Step 8: Build to verify the component compiles**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln`

Expected: PASS, whole suite green. `PayeePareto` is not rendered anywhere yet
— Task 14 places it — so this step is proving it compiles, not that it draws.

- [ ] **Step 9: Commit**

```bash
git add BillsMinimalApi.Contracts/NumberWords.cs BillsMinimalApi.Contracts/ParetoRows.cs tests/BillsMinimalApi.UnitTests/ParetoRowTests.cs bills-frontend/BillsFrontEndBlazor/Shared/PayeePareto.razor bills-frontend/BillsFrontEndBlazor/Shared/PayeePareto.razor.css
git commit -m "Rank payees by what they cost you, with the running share"
```

---

### Task 14: The paid-rate strip, and Reports rebuilt around four numbers

Idea 10, plus the composition the other nine ideas leave behind. Reports today
is eight stat cards and four tables: overdue aging, what to pay next, the payee
breakdown, month by month, and bill size distribution. After Tasks 7–9 two of
those live on Overview instead — `AgingStrip` took the aging table and
`LateBillsList` took "what to pay next" — so this task deletes them here rather
than re-skinning two copies of the same thing.

What Reports becomes:

1. Four headline cards — Total billed, Paid, Outstanding, Overdue — still
   counting up through the existing `AnimatedCounter`.
2. The typical/mean/largest line, which is the four cards that got cut turned
   into one sentence of prose.
3. The size-band sentence, which is the bill-size table turned into another.
4. `PaidRateStrip` — the month-by-month table's one interesting column, as a
   row of shaded cells.
5. `PayeePareto` from Task 13.

Three things go away and are worth naming:

- **The month-by-month table.** Its columns were bills / billed / paid / paid
  rate; the strip keeps the rate, which is the only one that says something the
  headline figures do not.
- **The bill-size distribution table.** Five rows, one sentence.
- **The expand/collapse on the payee table.** `PayeePreviewRows`,
  `_showAllPayees` and `HiddenPayeeCount` go with it — a Pareto table is read
  from the top and stops being interesting well before its last row, so there
  is nothing to reveal.

**Files:**
- Create: `BillsMinimalApi.Contracts/SizeBandSentence.cs`
- Create: `tests/BillsMinimalApi.UnitTests/SizeBandSentenceTests.cs`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/PaidRateStrip.razor`
- Create: `bills-frontend/BillsFrontEndBlazor/Shared/PaidRateStrip.razor.css`
- Create: `bills-frontend/BillsFrontEndBlazor/Pages/Reports.razor.css`
- Modify: `bills-frontend/BillsFrontEndBlazor/Pages/Reports.razor:1-475` (whole file)
- Modify: `bills-frontend/BillsFrontEndBlazor/Pages/Reports.razor.cs:1-406` (whole file)

**Interfaces:**
- Consumes:
  - `NumberWords.Spell(int)` — Task 13.
  - `<PayeePareto Payees="@Summary.Payees" />` — Task 13.
  - `<Icon Name="…" Size="…" Class="…" />` — Task 6.
  - `SizeBand` — `string Label`, `int Count`, `decimal Total`. Labels come from
    `BillSummaryBuilder.SizeBandLabels`: `Under $50`, `$50 – $99` (en dash,
    U+2013, with a space either side), `$100 – $249`, `$250 – $499`,
    `$500 and over`.
  - `MonthTotals` — `int Year`, `int Month`, `int Bills`, `decimal Billed`,
    `decimal Paid`, `decimal Outstanding`, `double PaidPercent`,
    `DateTime FirstDay`. `BillSummary.Months` arrives **newest month first**.
  - `AnimatedCounter` — existing, `Value` (`decimal`), `Format` (`string`),
    `Generation` (`int`).
- Produces:
  - `SizeBandSentence.Describe(IReadOnlyList<SizeBand>? bands) -> string?`
  - `SizeBandSentence.Phrase(string? label) -> string`
  - `<PaidRateStrip Months="@Summary.Months" />`
  - Nothing else: Task 15 touches the shell, not this page.

- [ ] **Step 1: Write the failing sentence tests**

Create `tests/BillsMinimalApi.UnitTests/SizeBandSentenceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter SizeBandSentenceTests`

Expected: FAIL to compile — `The name 'SizeBandSentence' does not exist in the current context`.

- [ ] **Step 3: Write the sentence**

Create `BillsMinimalApi.Contracts/SizeBandSentence.cs`:

```csharp
using System.Globalization;

namespace BillsMinimalApi.Contracts;

/// <summary>
/// The bill-size distribution as one sentence rather than five rows.
/// <para>
/// The table it replaces answered "how are my bills distributed by size",
/// which nobody asks. The question underneath it — "where is the money" — has
/// a one-line answer, so that is what this returns: the band holding the most
/// money, how many bills are in it, and what share of the total it is.
/// </para>
/// </summary>
public static class SizeBandSentence
{
    /// <summary>The separator <c>BillSummaryBuilder.SizeBandLabels</c> uses
    /// between the two ends of a range. An en dash, not a hyphen.</summary>
    private const char EnDash = '–';

    private const string UnderPrefix = "Under ";

    private const string OverSuffix = " and over";

    /// <summary>
    /// Names the band holding the most money, or null when there is nothing to
    /// say — no bands, no bills, or no money in any of them.
    /// </summary>
    public static string? Describe(IReadOnlyList<SizeBand>? bands)
    {
        if (bands is null || bands.Count == 0)
        {
            return null;
        }

        var billCount = bands.Sum(b => b.Count);
        var money = bands.Sum(b => b.Total);

        if (billCount == 0 || money <= 0m)
        {
            return null;
        }

        // OrderByDescending is stable over a list, so a tie keeps the server's
        // order — which runs smallest band to largest. Naming the smaller of
        // two equally expensive bands is the more surprising fact of the two,
        // and surprise is the whole point of the sentence.
        var top = bands.OrderByDescending(b => b.Total).First();
        var phrase = Phrase(top.Label);

        if (billCount == 1)
        {
            return $"The only bill in this range is {phrase}.";
        }

        // "Twelve of 12 bills" is a sentence that makes a reader stop and
        // check, for no gain — and the money share is trivially 100%.
        if (top.Count == billCount)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "All {0} bills sit {1}.",
                billCount,
                phrase);
        }

        var share = (double)(top.Total / money) * 100d;

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} of {1} bills sit {2} — that band is {3:0}% of the money.",
            NumberWords.Spell(top.Count),
            billCount,
            phrase,
            share);
    }

    /// <summary>
    /// Turns a band label into something that reads inside a sentence:
    /// <c>$250 – $499</c> becomes <c>between $250 and $499</c>.
    /// </summary>
    /// <remarks>
    /// Shape-matched rather than label-matched, so rewording a band server-side
    /// degrades to "at &lt;whatever it now says&gt;" instead of falling through
    /// to a case that no longer exists.
    /// </remarks>
    public static string Phrase(string? label)
    {
        var text = (label ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return "in that band";
        }

        var dash = text.IndexOf(EnDash);

        if (dash > 0)
        {
            var low = text[..dash].Trim();
            var high = text[(dash + 1)..].Trim();

            return $"between {low} and {high}";
        }

        if (text.StartsWith(UnderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return "under " + text[UnderPrefix.Length..];
        }

        return "at " + text.Replace(OverSuffix, " or more", StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/BillsMinimalApi.UnitTests --filter SizeBandSentenceTests`

Expected: PASS — 17 test cases green (9 facts plus 8 theory cases).

- [ ] **Step 5: Build the paid-rate strip**

Create `bills-frontend/BillsFrontEndBlazor/Shared/PaidRateStrip.razor`:

```razor
@using System.Globalization

@* Idea 10: the month-by-month table's paid-rate column, as one row of shaded
   cells. The shade is a token mix rather than a scale of its own — the accent
   blended into the sunken background, more accent the higher the rate — so
   the strip re-tints itself with the palette instead of pinning five hexes
   that would only be right in one of the four themes. *@

<section class="paid-rate">

    <h2 class="title">Paid rate by month</h2>
    <p class="subtitle">Share of each month's money that has actually been paid.</p>

    @if (_months.Count == 0)
    {
        <p class="empty">No months in this range yet.</p>
    }
    else
    {
        <ol class="strip">
            @foreach (var month in _months)
            {
                <li class="month">
                    <span class="cell" style="@CellStyle(month.PaidPercent)" title="@Tooltip(month)">
                        @month.PaidPercent.ToString("0")%
                    </span>
                    <span class="label">@Label(month)</span>
                    <span class="billed">@month.Billed.ToString("C0")</span>
                </li>
            }
        </ol>

        @if (_hidden > 0)
        {
            <p class="trimmed">
                @_hidden earlier @(_hidden == 1 ? "month is" : "months are") outside the strip.
                Narrow the range to see them.
            </p>
        }
    }

</section>

@code {
    /// <summary>
    /// How many months fit before the cells stop being readable. All-time on a
    /// long-lived account is dozens of months, and thirty 12px slivers is not
    /// a chart. The strip trims rather than scrolls, and says so.
    /// </summary>
    private const int MaxMonths = 24;

    /// <summary>The rate at which a cell is dark enough that muted text on it
    /// stops being legible.</summary>
    private const double HighlightAt = 80d;

    [Parameter, EditorRequired]
    public List<MonthTotals> Months { get; set; } = new();

    private List<MonthTotals> _months = new();

    private int _hidden;

    private bool _spansYears;

    protected override void OnParametersSet()
    {
        // The response is newest month first, and a timeline reads left to
        // right, so this is reversed — after trimming, which keeps the most
        // recent months rather than the oldest.
        _hidden = Math.Max(0, Months.Count - MaxMonths);

        _months = Months
            .Take(MaxMonths)
            .Reverse()
            .ToList();

        _spansYears = _months.Select(m => m.Year).Distinct().Count() > 1;
    }

    /// <summary>
    /// "Mar" is enough until the strip crosses a new year, at which point two
    /// Marches would sit side by side with nothing to tell them apart.
    /// </summary>
    private string Label(MonthTotals month) =>
        month.FirstDay.ToString(_spansYears ? "MMM yy" : "MMM", CultureInfo.CurrentCulture);

    private static string Tooltip(MonthTotals month) =>
        $"{month.FirstDay.ToString("MMMM yyyy", CultureInfo.CurrentCulture)} — " +
        $"{month.Paid.ToString("C")} paid of {month.Billed.ToString("C")}";

    /// <summary>
    /// Invariant, because this ends up inside a style attribute: a culture
    /// that writes 12,5 would produce a color-mix the browser drops, and a
    /// dropped background is an invisible cell rather than a wrong one.
    /// </summary>
    private static string CellStyle(double rate)
    {
        // Twelve percent at a zero rate rather than nothing, so an unpaid
        // month still reads as a cell rather than as a gap in the strip.
        var mix = Math.Clamp(12d + (rate * 0.5d), 0d, 100d);

        return string.Format(
            CultureInfo.InvariantCulture,
            "background: color-mix(in srgb, var(--accent) {0:0.##}%, var(--sunken)); color: {1};",
            mix,
            rate >= HighlightAt ? "var(--accent-text)" : "var(--text)");
    }
}
```

- [ ] **Step 6: Style the strip**

Create `bills-frontend/BillsFrontEndBlazor/Shared/PaidRateStrip.razor.css`:

```css
.paid-rate {
    background: var(--surface);
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius-lg);
    padding: 1.25rem 1.5rem;
}

.title {
    color: var(--text);
    font-size: 1rem;
    font-weight: 600;
    margin: 0;
}

.subtitle,
.empty {
    color: var(--muted);
    font-size: .85rem;
    margin: .15rem 0 0;
}

.strip {
    display: flex;
    gap: 6px;
    list-style: none;
    margin: 1rem 0 0;
    padding: 0;
}

.month {
    display: flex;
    flex: 1 1 0;
    flex-direction: column;
    gap: .3rem;
    min-width: 0;
}

.cell {
    border-radius: var(--radius);
    display: grid;
    font-size: .92rem;
    font-variant-numeric: tabular-nums;
    font-weight: 500;
    height: 62px;
    place-items: center;
}

.label,
.billed {
    overflow: hidden;
    text-align: center;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.label {
    color: var(--muted);
    font-size: .74rem;
}

.billed {
    color: var(--faint);
    font-size: .72rem;
    font-variant-numeric: tabular-nums;
}

.trimmed {
    color: var(--faint);
    font-size: .78rem;
    margin: .8rem 0 0;
}
```

- [ ] **Step 7: Rewrite the Reports markup**

Replace the whole of `bills-frontend/BillsFrontEndBlazor/Pages/Reports.razor`
with:

```razor
@page "/reports"
@attribute [Authorize]

<PageTitle>Reports</PageTitle>

<div class="reports">

    <header class="page-head">
        <div>
            <h1>Reports</h1>
            <p class="lede">Where the money is, who it is owed to, and how late it is.</p>

            @* The server's date, not this machine's. Every figure on the page was
               computed against it, and the range presets below are cut from it —
               so if the two clocks ever disagree, this is the one that explains
               the numbers. Same line, same source, as the Overview. *@
            <p class="as-of">as of @Summary.AsOf.ToString("MMM d, yyyy")</p>
        </div>

        <div class="head-actions">
            <button type="button" class="ghost" @onclick="LoadBillsAsync" disabled="@_isLoading">
                <Icon Name="arrows-clockwise" Size="16" Class="me-1" />
                Refresh
            </button>

            @* A plain link, not a JS-driven download: the endpoint returns the
               file with a Content-Disposition, so this needs no interop — which
               is the only reason it works during the prerender pass. download
               is also what stops Blazor's router from intercepting the click and
               trying to route to a page that does not exist. *@
            <a class="ghost" href="@CsvHref" download>
                <Icon Name="download-simple" Size="16" Class="me-1" />
                Export CSV
            </a>
        </div>
    </header>

    @if (_loadFailed)
    {
        <div class="alert alert-danger d-flex align-items-center gap-3" role="alert">
            <Icon Name="warning-octagon" Size="22" />
            <div class="flex-grow-1">Could not reach the API.</div>
            <button class="btn btn-sm btn-outline-danger" @onclick="LoadBillsAsync">
                <Icon Name="arrows-clockwise" Size="16" Class="me-1" /> Retry
            </button>
        </div>
    }

    <div class="ranges-row">
        <div class="ranges" role="group" aria-label="Report date range">
            @foreach (var option in ReportRanges.All)
            {
                <button type="button"
                        class="range @(_range == option ? "on" : null)"
                        aria-pressed="@(_range == option ? "true" : "false")"
                        @onclick="() => SetRangeAsync(option)">
                    @option.Label()
                </button>
            }
        </div>

        @* Every section below reads from the same window, so the window itself
           has to be spelled out — "Last 6 months" alone does not say whether
           today is in it. *@
        <p class="caption">@RangeCaption</p>
    </div>

    @if (_isLoading && _summary is null)
    {
        <p class="loading">Loading reports…</p>
    }
    else if (BillCount == 0)
    {
        <p class="empty">
            @if (HasNoBillsAtAll)
            {
                <text>No bills yet. Create one and this page fills itself in.</text>
            }
            else
            {
                <text>No bills are due in this range. Try a wider one.</text>
            }
        </p>
    }
    else
    {
        <div class="@(_isLoading ? "body is-refreshing" : "body")">

            @* Four figures, not eight. The four that were cut — largest,
               average, median, due-in-30 — are numbers you read once and
               compare against each other, which is a sentence's job rather
               than a card's; they are the .typical line below.

               _animationGeneration is what replays the count-up when the range
               changes to a window that happens to total the same as the old
               one. See AnimatedCounter.Generation. *@
            <div class="figures">

                <article class="figure">
                    <span class="label">Total billed</span>
                    <span class="value">
                        <AnimatedCounter Value="@TotalBilled" Format="C"
                                         Generation="@_animationGeneration" />
                    </span>
                    <span class="note">across @BillCount @(BillCount == 1 ? "bill" : "bills")</span>
                </article>

                <article class="figure">
                    <span class="label">Paid</span>
                    <span class="value ok">
                        <AnimatedCounter Value="@PaidAmount" Format="C"
                                         Generation="@_animationGeneration" />
                    </span>
                    @* Share of money, not of bills — see PaidPercent. *@
                    <span class="note">@PaidPercent.ToString("0")% of the total</span>
                </article>

                <article class="figure">
                    <span class="label">Outstanding</span>
                    <span class="value">
                        <AnimatedCounter Value="@OutstandingAmount" Format="C"
                                         Generation="@_animationGeneration" />
                    </span>
                    <span class="note">@UnpaidCount unpaid</span>
                </article>

                <article class="figure">
                    <span class="label">Overdue</span>
                    <span class="value @(OverdueAmount > 0 ? "late" : null)">
                        <AnimatedCounter Value="@OverdueAmount" Format="C"
                                         Generation="@_animationGeneration" />
                    </span>
                    <span class="note">@OverdueCount past their due date</span>
                </article>

            </div>

            <p class="typical">
                <span class="fact">Typical bill <strong>@MedianBill.ToString("C")</strong> median</span>
                <span class="fact">Mean <strong>@AverageBill.ToString("C")</strong></span>
                @if (LargestBill is { } largest)
                {
                    <span class="fact">
                        Largest <strong>@largest.PaymentDue.ToString("C")</strong>
                        <span class="who">@largest.PayeeName</span>
                    </span>
                }
            </p>

            @if (_bandSentence is not null)
            {
                <p class="bands">@_bandSentence</p>
            }

            <PaidRateStrip Months="@Summary.Months" />

            <PayeePareto Payees="@Summary.Payees" />

        </div>
    }

</div>
```

- [ ] **Step 8: Rewrite the Reports code-behind**

Replace the whole of `bills-frontend/BillsFrontEndBlazor/Pages/Reports.razor.cs`
with:

```csharp
using BillsFrontEndBlazor.Services;
using BillsMinimalApi.Contracts;
using Microsoft.AspNetCore.Components;

namespace BillsFrontEndBlazor.Pages
{
    /// <summary>
    /// The reports page. Every figure on it is computed by Postgres and arrives
    /// in one <see cref="BillSummary"/> response.
    /// <para>
    /// It used to fetch the whole table and aggregate in C#, which was fine
    /// until the list endpoint started paging — at which point "every bill" was
    /// quietly ten of them, and a report is the one page that cannot be allowed
    /// to describe a page of data as though it were the set. Asking the server
    /// for the aggregates is both the fix and the faster answer.
    /// </para>
    /// <para>
    /// What is left here is loading and framing. The two charts own their own
    /// arithmetic — <c>PaidRateStrip</c> and <c>PayeePareto</c> take the raw
    /// rows off the summary — and the one sentence this page still computes,
    /// it computes through <see cref="SizeBandSentence"/>, which is unit-tested
    /// away from the renderer.
    /// </para>
    /// </summary>
    public partial class Reports : IDisposable
    {
        /// <summary>Stands in until the first response lands, so every headline
        /// property can read from a summary without a null check apiece.
        /// </summary>
        private static readonly BillSummary NoData = new();

        [Inject]
        public BillService BillService { get; set; } = default!;

        [Inject]
        public BillEventService BillEventService { get; set; } = default!;

        [Inject]
        public ToastService Toasts { get; set; } = default!;

        private BillSummary? _summary;
        private bool _isLoading = true;
        private bool _loadFailed;

        private ReportRange _range = ReportRange.AllTime;

        private string? _bandSentence;

        /// <summary>
        /// The date the figures on screen were computed against — the server's,
        /// taken from <see cref="BillSummary.AsOf"/>, not this machine's. They
        /// are usually the same day, and when they are not it is the response
        /// that decides what "3 days late" means, because the response is where
        /// the number came from.
        /// </summary>
        private DateTime _today = DateTime.Today;

        /// <summary>Bumped on every load so the headline counters replay from
        /// zero. Without it, switching to a range whose totals happen to match
        /// the previous one — or refreshing unchanged data — animates nothing.
        /// See <c>AnimatedCounter.Generation</c>.</summary>
        private int _animationGeneration;

        /// <summary>Which load is the current one; see the same field in
        /// Bills.razor.cs. Clicking through the range presets faster than the
        /// server answers is the case this exists for.</summary>
        private int _loadGeneration;

        private BillSummary Summary => _summary ?? NoData;

        protected override async Task OnInitializedAsync()
        {
            BillEventService.OnBillsChanged += OnBillsChanged;
            await LoadBillsAsync();
        }

        public void Dispose()
        {
            BillEventService.OnBillsChanged -= OnBillsChanged;
        }

        private void OnBillsChanged()
        {
            // Never `async void` — see Bills.razor.cs. InvokeAsync also puts the
            // work back on the circuit's synchronization context.
            _ = InvokeAsync(LoadBillsAsync);
        }

        /// <summary>
        /// Kept parameterless so the markup can bind it directly to the Refresh
        /// and Retry buttons.
        /// </summary>
        private async Task LoadBillsAsync()
        {
            var generation = ++_loadGeneration;

            _isLoading = true;
            _loadFailed = false;
            StateHasChanged();

            try
            {
                // The window is resolved against this machine's date to ask the
                // question; the answer comes back stamped with the date it was
                // actually computed against, and that is what gets rendered.
                var (from, to) = _range.Window(DateTime.Today);

                var summary = await BillService.GetSummaryAsync(from, to);

                if (generation != _loadGeneration)
                {
                    return;
                }

                _summary = summary;
                _today = summary.AsOf;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (generation != _loadGeneration)
                {
                    return;
                }

                _summary = NoData;
                _today = DateTime.Today;
                _loadFailed = true;
                Toasts.ShowError("Could not load reports. Is the API running?");
            }
            finally
            {
                if (generation == _loadGeneration)
                {
                    Rebuild();
                    _isLoading = false;
                    StateHasChanged();
                }
            }
        }

        private Task SetRangeAsync(ReportRange range)
        {
            if (_range == range)
            {
                return Task.CompletedTask;
            }

            _range = range;

            return LoadBillsAsync();
        }

        /// <summary>
        /// The per-response work that is not a component's: replay the
        /// counters, and re-read the size-band sentence. One place, so the two
        /// can never be done for different responses.
        /// </summary>
        private void Rebuild()
        {
            _animationGeneration++;
            _bandSentence = SizeBandSentence.Describe(Summary.SizeBands);
        }

        // -- Range framing --------------------------------------------------

        private string RangeCaption => _range.Caption(_today);

        private string CsvHref => $"reports/bills.csv?range={_range.Slug()}";

        /// <summary>
        /// "All time and nothing in it" is the only way to be sure there are no
        /// bills at all rather than none in this window — which is the
        /// difference between offering to create one and suggesting a wider
        /// range.
        /// </summary>
        private bool HasNoBillsAtAll => _range == ReportRange.AllTime && BillCount == 0;

        // -- Headline figures -----------------------------------------------

        private int BillCount => Summary.BillCount;

        private decimal TotalBilled => Summary.TotalBilled;

        private decimal PaidAmount => Summary.PaidAmount;

        private decimal OutstandingAmount => Summary.OutstandingAmount;

        private int UnpaidCount => Summary.UnpaidCount;

        private double PaidPercent => Summary.PaidPercent;

        private int OverdueCount => Summary.OverdueCount;

        private decimal OverdueAmount => Summary.OverdueAmount;

        private SummaryBill? LargestBill => Summary.LargestBill;

        private decimal AverageBill => Summary.AverageBill;

        private decimal MedianBill => Summary.MedianBill;
    }
}
```

- [ ] **Step 9: Style the page**

Create `bills-frontend/BillsFrontEndBlazor/Pages/Reports.razor.css`:

```css
.reports {
    display: flex;
    flex-direction: column;
    gap: 1.5rem;
    margin: 0 auto;
    max-width: 1240px;
    padding: 2rem 1.5rem 4rem;
}

.page-head {
    align-items: flex-start;
    display: flex;
    justify-content: space-between;
}

.page-head h1 {
    color: var(--text);
    font-size: 1.5rem;
    font-weight: 600;
    margin: 0;
}

.lede {
    color: var(--muted);
    font-size: .9rem;
    margin: .2rem 0 0;
}

/* Same values as the Overview's rule. Two screens, one date line, one look —
   duplicated rather than hoisted because scoped CSS cannot be shared between
   components, and a global class for six declarations would be worse. */
.as-of {
    color: var(--faint);
    font-size: .82rem;
    margin: .1rem 0 0;
}

.head-actions {
    display: flex;
    gap: .5rem;
}

.ghost {
    align-items: center;
    background: none;
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius);
    color: var(--muted);
    cursor: pointer;
    display: inline-flex;
    padding: .4rem .8rem;
    text-decoration: none;
}

.ghost:hover:not(:disabled) { color: var(--text); }

.ghost:disabled {
    cursor: default;
    opacity: .5;
}

/* One control, one border: the presets read as a segmented switch rather than
   five separate buttons that happen to be adjacent. */
.ranges {
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius);
    display: inline-flex;
    overflow: hidden;
}

.range {
    background: none;
    border: 0;
    border-right: var(--border-width) solid var(--border);
    color: var(--muted);
    cursor: pointer;
    font-size: .82rem;
    padding: .4rem .85rem;
}

.range:last-child { border-right: 0; }

.range:hover { color: var(--text); }

.range.on {
    background: var(--sunken);
    color: var(--accent-text);
}

.caption,
.loading,
.empty {
    color: var(--muted);
    font-size: .82rem;
    margin: .5rem 0 0;
}

.empty {
    background: var(--surface);
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius-lg);
    margin: 0;
    padding: 3rem 1.5rem;
    text-align: center;
}

.body {
    display: flex;
    flex-direction: column;
    gap: 1.5rem;
}

/* A refresh over data already on screen dims rather than blanks: the numbers
   stay readable and stop being trusted, which is what is actually true. */
.is-refreshing { opacity: .6; }

.figures {
    display: grid;
    gap: 1rem;
    grid-template-columns: repeat(4, minmax(0, 1fr));
}

.figure {
    background: var(--surface);
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius-lg);
    display: flex;
    flex-direction: column;
    gap: .25rem;
    padding: 1.1rem 1.25rem;
}

.figure .label {
    color: var(--muted);
    font-size: .72rem;
    letter-spacing: .07em;
    text-transform: uppercase;
}

.figure .value {
    color: var(--text);
    font-size: 1.6rem;
    font-variant-numeric: tabular-nums;
    font-weight: 600;
}

.figure .value.ok { color: var(--ok); }

.figure .value.late { color: var(--late); }

.figure .note {
    color: var(--faint);
    font-size: .78rem;
}

/* The four stat cards that got cut, as prose. */
.typical {
    color: var(--muted);
    display: flex;
    flex-wrap: wrap;
    font-size: .88rem;
    gap: .35rem 2rem;
    margin: 0;
}

.typical strong {
    color: var(--text);
    font-variant-numeric: tabular-nums;
    font-weight: 600;
}

.typical .who { color: var(--faint); }

.bands {
    color: var(--muted);
    font-size: .88rem;
    margin: -.75rem 0 0;
}
```

- [ ] **Step 10: Run the suite**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln`

Expected: PASS, whole suite green — the Blazor project builds as part of the
solution, so a stale reference to one of the deleted members
(`PayeeSortColumn`, `_showAllPayees`, `Width`, `MonthLabel`, `PriorityNote`,
`DueDateText`, `AgingRow`, `BandRow`) fails the build here rather than at
runtime. If it does, the leftover is in `Reports.razor` — step 7 replaces the
whole file, so a partial replacement is the likely cause.

- [ ] **Step 11: Commit**

```bash
git add BillsMinimalApi.Contracts/SizeBandSentence.cs tests/BillsMinimalApi.UnitTests/SizeBandSentenceTests.cs bills-frontend/BillsFrontEndBlazor/Shared/PaidRateStrip.razor bills-frontend/BillsFrontEndBlazor/Shared/PaidRateStrip.razor.css bills-frontend/BillsFrontEndBlazor/Pages/Reports.razor bills-frontend/BillsFrontEndBlazor/Pages/Reports.razor.cs bills-frontend/BillsFrontEndBlazor/Pages/Reports.razor.css
git commit -m "Rebuild Reports around four numbers, two sentences and two charts"
```

---

### Task 15: Re-skin the shell, and clear out what the rebuild left behind

The three screens are done; the frame around them is still the old app. The
sidebar is a hardcoded `#151a26` that ignores the palette entirely, the nav's
active state is the blue-to-purple gradient the redesign replaces, and
`site.css` is 640 lines of which most now styles markup that no longer exists —
the dashboard's donut and bar chart (Task 8 replaced them), the bills table
(Task 9), and every Reports table (Task 14).

This is the task that makes the Global Constraint true: **no hex outside
`tokens.css`**. Step 7 is the grep that proves it.

Two things this task deliberately does **not** do:

- **Design a mobile layout.** The handoff says the redesign is desktop-first at
  ~1240px and that mobile is not yet designed. The existing drawer and rail keep
  working — they are re-skinned here, not redrawn — and step 9 writes down what
  a mobile pass would have to decide.
- **Touch the account pages' structure.** Sign in / register / sign out are not
  among the ten ideas. Their colours are re-expressed in tokens so all four
  themes reach them, and nothing else about them changes.

**Files:**
- Modify: `bills-frontend/BillsFrontEndBlazor/wwwroot/css/tokens.css` (add one token)
- Modify: `bills-frontend/BillsFrontEndBlazor/Shared/MainLayout.razor:35`
- Modify: `bills-frontend/BillsFrontEndBlazor/Shared/MainLayout.razor.css`
- Modify: `bills-frontend/BillsFrontEndBlazor/Shared/NavMenu.razor.css`
- Modify: `bills-frontend/BillsFrontEndBlazor/wwwroot/css/site.css:1-640` (whole file)
- Create: `docs/mobile-layout-follow-up.md`

**Interfaces:**
- Consumes: every token from Task 5 — `--bg --surface --sunken --text --muted
  --faint --border --accent --accent-text --late --ok --radius --radius-lg
  --border-width --font-sans`.
- Produces: `--scrim`, used only by the mobile drawer backdrop. Nothing later
  in the plan depends on this task; it is the last one.

- [ ] **Step 1: Add the scrim token**

The drawer backdrop is the one surface in the app that is neither a token colour
nor allowed to be a literal. Add it to the palette-independent block in
`bills-frontend/BillsFrontEndBlazor/wwwroot/css/tokens.css` — the one holding
`--radius`, `--radius-lg` and `--border-width`, above the four
`[data-palette][data-mode]` blocks:

```css
    /* The wash behind the mobile drawer. Dark in both modes deliberately: a
       scrim's job is to push the page back, and a pale one over a light page
       pushes nothing. It is the only colour in the app that does not vary by
       palette, which is why it sits up here with the metrics rather than in the
       four blocks below. */
    --scrim: rgba(0, 0, 0, .5);
```

- [ ] **Step 2: Re-skin the shell**

In `bills-frontend/BillsFrontEndBlazor/Shared/MainLayout.razor.css`, replace
the four declarations that name colours. Everything else in the file — the
comment about why the document is the scroller, the rail widths, the drawer
transforms, the z-index ladder — is unchanged.

Replace, in `.sidebar`:

```css
    background: #151a26;
    border-right: 1px solid rgba(255, 255, 255, 0.06);
```

with:

```css
    background: var(--surface);
    border-right: var(--border-width) solid var(--border);
```

Replace, in `.page.drawer-open .drawer-backdrop`:

```css
        background: rgba(0, 0, 0, 0.5);
```

with:

```css
        background: var(--scrim);
```

Replace, in `.mobile-bar`:

```css
        background: #151a26;
        color: #fff;
```

with:

```css
        background: var(--surface);
        border-bottom: var(--border-width) solid var(--border);
        color: var(--text);
```

Replace the whole `.mobile-bar-toggle` pair:

```css
    .mobile-bar-toggle {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 2.5rem;
        height: 2.5rem;
        padding: 0;
        border: 0;
        border-radius: .5rem;
        background: transparent;
        color: #fff;
        font-size: 1.35rem;
        line-height: 1;
    }

    .mobile-bar-toggle:hover,
    .mobile-bar-toggle:focus-visible {
        background: rgba(255, 255, 255, 0.12);
    }
```

with:

```css
    .mobile-bar-toggle {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 2.5rem;
        height: 2.5rem;
        padding: 0;
        border: 0;
        border-radius: var(--radius);
        background: transparent;
        color: var(--muted);
        font-size: 1.35rem;
        line-height: 1;
    }

    .mobile-bar-toggle:hover,
    .mobile-bar-toggle:focus-visible {
        background: var(--sunken);
        color: var(--text);
    }
```

- [ ] **Step 3: Stop the content pane double-padding every page**

Each of the three screens now owns its own gutter — `.overview`, `.bills` and
`.reports` all set `max-width: 1240px; padding: 2rem 1.5rem 4rem`. The shell
adding `px-4` on top of that pushes every page 1.5rem off its own centre line.

In `bills-frontend/BillsFrontEndBlazor/Shared/MainLayout.razor:35`, replace:

```razor
        <article class="content px-4">
```

with:

```razor
        @* No padding class: every page sets its own gutter and max-width, and
           a second one here would offset all three off centre. *@
        <article class="content">
```

- [ ] **Step 4: Re-skin the navigation**

In `bills-frontend/BillsFrontEndBlazor/Shared/NavMenu.razor.css`, replace each
of these blocks. The rail media queries at the bottom of the file name no
colours and are unchanged.

Replace `.nav-shell`:

```css
.nav-shell {
    display: flex;
    flex-direction: column;
    height: 100%;
    color: #9aa3b8;
}
```

with:

```css
.nav-shell {
    display: flex;
    flex-direction: column;
    height: 100%;
    color: var(--muted);
}
```

Replace the `border-bottom` in `.nav-brand`:

```css
    border-bottom: 1px solid rgba(255, 255, 255, 0.06);
```

with:

```css
    border-bottom: var(--border-width) solid var(--border);
```

Replace the `color` in `.nav-brand-link`:

```css
    color: #fff;
```

with:

```css
    color: var(--text);
```

Replace `.nav-brand-link i`:

```css
.nav-brand-link i {
    font-size: 1.25rem;
    color: #6ea8fe;
}
```

with:

```css
.nav-brand-link i {
    font-size: 1.25rem;
    color: var(--accent);
}
```

Replace the two colour lines in `.nav-rail-toggle`:

```css
    background: rgba(255, 255, 255, 0.07);
    color: #9aa3b8;
```

with:

```css
    background: var(--sunken);
    color: var(--muted);
```

Replace the `.nav-rail-toggle` hover pair's body:

```css
.nav-rail-toggle:hover,
.nav-rail-toggle:focus-visible {
    background: rgba(255, 255, 255, 0.16);
    color: #fff;
}
```

with:

```css
.nav-rail-toggle:hover,
.nav-rail-toggle:focus-visible {
    background: var(--sunken);
    color: var(--text);
}
```

Replace the `border-radius` and `color` in `.nav-links ::deep .nav-link`:

```css
    border-radius: .5rem;
    color: #9aa3b8;
```

with:

```css
    border-radius: var(--radius);
    color: var(--muted);
```

Replace the hover:

```css
.nav-links ::deep .nav-link:hover {
    background: rgba(255, 255, 255, 0.07);
    color: #fff;
}
```

with:

```css
.nav-links ::deep .nav-link:hover {
    background: var(--sunken);
    color: var(--text);
}
```

Replace the active state — the gradient is the single most visible thing the
redesign removes:

```css
.nav-links ::deep .nav-link.active {
    /* Same blue-to-purple ramp as the dashboard hero, so the active page reads
       as part of the app's palette rather than a generic highlight. */
    background: linear-gradient(135deg, #0d6efd, #6f42c1);
    color: #fff;
}
```

with:

```css
/* A tint and a marker rather than a fill. The accent is a line colour in this
   design — flooding a 15rem-wide block with it is the one thing the Nocturne
   spec rules out by name — so the active page is marked the way the rest of the
   app marks things: a bar against the edge, and the accent's text shade. */
.nav-links ::deep .nav-link.active {
    background: var(--sunken);
    color: var(--accent-text);
    position: relative;
}

/* An absolutely positioned bar, not a border-left: a border would take 2px out
   of the link's width and shift its icon and label sideways the moment the page
   changed. */
.nav-links ::deep .nav-link.active::before {
    background: var(--accent);
    border-radius: 0 2px 2px 0;
    bottom: .5rem;
    content: "";
    left: 0;
    position: absolute;
    top: .5rem;
    width: 2px;
}
```

Replace the `border-top` in `.nav-account`:

```css
    border-top: 1px solid rgba(255, 255, 255, 0.06);
```

with:

```css
    border-top: var(--border-width) solid var(--border);
```

Replace the `border-radius` and `color` in `.nav-signout`:

```css
    border-radius: .5rem;
    background: transparent;
    color: #9aa3b8;
```

with:

```css
    border-radius: var(--radius);
    background: transparent;
    color: var(--muted);
```

Replace the `.nav-signout` hover:

```css
.nav-signout:hover,
.nav-signout:focus-visible {
    background: rgba(255, 255, 255, 0.07);
    color: #fff;
}
```

with:

```css
.nav-signout:hover,
.nav-signout:focus-visible {
    background: var(--sunken);
    color: var(--text);
}
```

- [ ] **Step 5: Rewrite the global stylesheet**

Three of `site.css`'s five sections style markup that no longer exists. Replace
the whole of `bills-frontend/BillsFrontEndBlazor/wwwroot/css/site.css` with:

```css
/* Global styles: the framework's own furniture, plus the account pages.
   Everything else the app draws is scoped to the component that draws it.

   tokens.css declares the values for the four palette × mode combinations; this
   file applies them. That split is the whole reason there are no literal
   colours below — a hex here would be right in one of the four themes and
   wrong in the other three.

   Three sections used to live here and no longer do:

   - Dashboard — the hero banner, action tiles, donut and bar chart. Overview is
     an obligation sentence, a weekly timeline, a late list and an aging strip
     now, and each of those carries its own scoped stylesheet.
   - Bills table — the six-column table, its phone card layout, the sortable
     headers and the filter group. Bills is due-window sections now; see
     Bills.razor.css and BillGroup.razor.css.
   - Reports — the eight stat cards, the four report tables, the aging ramp and
     the sticky first column. Reports is four figures, two sentences and two
     charts now; see Reports.razor.css, PaidRateStrip.razor.css and
     PayeePareto.razor.css. */

html, body {
    font-family: var(--font-sans);
}

body {
    background: var(--bg);
    color: var(--text);
}

h1:focus {
    outline: none;
}

a, .btn-link {
    color: var(--accent-text);
}

/* Outline, never a flood fill — the accent is a line colour in this design.
   Bootstrap's .btn-primary still renders in two places, the new-bill modal and
   the account pages, and a solid accent block in either one is the exact thing
   the Nocturne palette rules out. */
.btn-primary {
    background-color: transparent;
    border-color: var(--accent);
    color: var(--accent-text);
}

.btn-primary:hover,
.btn-primary:focus,
.btn-primary:active {
    background-color: var(--sunken);
    border-color: var(--accent);
    color: var(--accent-text);
}

.valid.modified:not([type=checkbox]) {
    outline: var(--border-width) solid var(--ok);
}

.invalid {
    outline: var(--border-width) solid var(--late);
}

.validation-message {
    color: var(--late);
}

/* ---------------------------------------------------------------------------
   Framework error surfaces
   --------------------------------------------------------------------------- */

/* Blazor shows this itself when the circuit drops; the markup is in _Host.cshtml
   and is not ours to change. Restyled from lightyellow to the app's own surface
   so a dropped connection does not also look like a rendering bug. */
#blazor-error-ui {
    background: var(--surface);
    border-top: var(--border-width) solid var(--late);
    bottom: 0;
    color: var(--text);
    display: none;
    left: 0;
    padding: 0.6rem 1.25rem 0.7rem 1.25rem;
    position: fixed;
    width: 100%;
    z-index: 1000;
}

    #blazor-error-ui .dismiss {
        cursor: pointer;
        position: absolute;
        right: 0.75rem;
        top: 0.5rem;
    }

/* The template ships this as white-on-#b32121 with an inline base64 warning
   triangle. Both go: the fill is a flood of a colour that is not in any of the
   four palettes, and the icon is a second icon set for the sake of one glyph. */
.blazor-error-boundary {
    background: var(--surface);
    border: var(--border-width) solid var(--late);
    border-radius: var(--radius);
    color: var(--late);
    padding: 1rem;
}

    .blazor-error-boundary::after {
        content: "An error has occurred."
    }

/* ---------------------------------------------------------------------------
   Toasts
   --------------------------------------------------------------------------- */

/* Bootstrap's .toast-container is pointer-events: none so it does not swallow
   clicks on the page beneath; the toasts themselves opt back in. */
.toast-host {
    z-index: 1090;
}

/* ---------------------------------------------------------------------------
   Account pages (sign in, register, sign out)
   --------------------------------------------------------------------------- */

/* These render under _AccountLayout, which has none of the app chrome — no nav
   rail, no content gutter — so the centring has to happen here.

   The gradient that used to fill .account-body was the dashboard hero's, and the
   hero is gone. What replaces it is what the rest of the app does: the page
   background, with a bordered card on it. _AccountLayout carries the same
   data-palette and data-mode attributes as _Host, so the sign-in screen is
   already in whichever theme the visitor last chose. */
.account-body {
    background: var(--bg);
    min-height: 100vh;
}

.account-shell {
    align-items: center;
    display: flex;
    justify-content: center;
    min-height: 100vh;
    padding: 2rem 1rem;
}

.account-card {
    background: var(--surface);
    border: var(--border-width) solid var(--border);
    border-radius: var(--radius-lg);
    max-width: 26rem;
    padding: 2rem;
    width: 100%;
}

.account-brand {
    align-items: center;
    color: var(--accent-text);
    display: flex;
    font-weight: 600;
    gap: .5rem;
    margin-bottom: 1.5rem;
}

    .account-brand i {
        font-size: 1.5rem;
    }

.account-title {
    font-size: 1.5rem;
    font-weight: 600;
    margin-bottom: .25rem;
}

.account-subtitle {
    color: var(--muted);
    font-size: .9375rem;
    margin-bottom: 1.5rem;
}

.account-alt {
    color: var(--muted);
    font-size: .9375rem;
    margin: 1.25rem 0 0;
    text-align: center;
}

/* Set apart from the form rather than styled as another alert: it is a hint for
   someone browsing, not something that went wrong. */
.account-demo {
    border-top: var(--border-width) solid var(--border);
    color: var(--muted);
    font-size: .8125rem;
    margin-top: 1.25rem;
    padding-top: 1rem;
}

    .account-demo code {
        color: var(--accent-text);
    }
```

- [ ] **Step 6: Run the suite**

Run: `dotnet test BillsMinimalApi/BillsMinimalApi.sln`

Expected: PASS, whole suite green. CSS changes cannot fail a test, but the
`MainLayout.razor` edit in step 3 can fail the Razor compile — which is exactly
why this runs before the manual checks below rather than after them.

- [ ] **Step 7: Prove no colour escaped the token layer**

Run, from the repository root:

```bash
grep -rnE '#[0-9a-fA-F]{3}\b|#[0-9a-fA-F]{6}\b|rgba?\(|hsla?\(' \
  --include='*.css' --include='*.razor' --include='*.cshtml' \
  bills-frontend/BillsFrontEndBlazor \
  | grep -v '/obj/' \
  | grep -v '/bin/' \
  | grep -v 'wwwroot/lib/' \
  | grep -v 'wwwroot/css/phosphor/'
```

Expected: every line is in `wwwroot/css/tokens.css`. Nothing else.

If a component stylesheet appears, the fix is to name the token that colour
should have been — not to add a token so the literal can stay. The palette has
`--age-1` through `--age-5` for ramps, `--late` and `--ok` for status, and
`color-mix(in srgb, var(--accent) N%, var(--sunken))` for a shade of the accent;
between them there is no colour in this design that needs a new name.

- [ ] **Step 8: Walk the four themes**

Run the API and the frontend, sign in as the demo account, and check each screen
in all four combinations of the two toggles: Nocturne dark (the default),
Nocturne light, Current light, Current dark.

```bash
dotnet run --project BillsMinimalApi
```

```bash
dotnet run --project bills-frontend/BillsFrontEndBlazor
```

At ≥1240px, on each of Overview, Bills and Reports, confirm:

1. Nothing is invisible — no text the same colour as what it sits on, and no
   card that has lost its border and merged into the page.
2. The active nav item is the tinted row with the accent bar, in every theme.
3. Reloading on a dark theme does not flash white first. That is the inline
   script from Task 5; if it flashes, the script is running too late in `<head>`
   rather than anything in this task.
4. The accent is never a large filled block — the nav's active row, the range
   presets, the Pareto bars and the paid-rate cells are all either outlines or
   `color-mix` tints.
5. The obligation sentence, the timeline's today marker, the aging strip, the
   bill groups and both Reports charts all recolour when the toggles move.

Then narrow the window to 900px and to 375px on each screen and confirm only
that it still **works**: no horizontal scrollbar on the document, the drawer
opens and closes, every control is reachable, and nothing overlaps to the point
of being unclickable. It will not look designed. That is step 9.

- [ ] **Step 9: Write down the mobile follow-up**

The handoff asks for this explicitly: *"Mobile/responsive layout is not yet
designed — flag this as a follow-up, don't guess a mobile treatment while
building this pass."*

Create `docs/mobile-layout-follow-up.md`:

```markdown
# Follow-up: mobile layout for the redesign

The Bills Manager redesign is designed for ≥1240px. The design handoff
(`design_handoff_bills_manager_redesign/README.md`) says so in as many words,
and asks that no mobile treatment be guessed at while building the desktop pass.
This is that flag.

## What exists today below 1240px

The shell still works. The sidebar becomes a drawer under 641px with a backdrop
and a top bar, exactly as it did before the redesign — re-skinned in tokens,
not redrawn. Every screen degrades by reflowing rather than by breaking: the
grids collapse to fewer columns, and nothing is clipped or unreachable.

It is not designed. Several things are merely tolerable:

- **Reports' four headline figures** sit in a four-column grid that becomes
  four narrow columns rather than two rows of two.
- **The paid-rate strip** divides its width by the number of months. At a
  twelve-month range on a phone that is roughly 25px a cell, which is a row of
  slivers rather than a chart. It already trims to 24 months and says so; on a
  phone the useful number is nearer six.
- **The weekly cash-flow timeline** is a fixed-height SVG scaled to the
  container. At phone widths the week ticks overlap.
- **The Pareto table** has four columns, one of which is a bar. On a phone the
  bar column is the first thing that should go.
- **Inline editing** puts a text input in a table cell sized for desktop.

## What a mobile pass has to decide

1. Whether the bill groups stay tables or become cards, as the old bills table
   did below 768px. Cards cost the alignment that makes a column of money
   readable; tables cost horizontal space there is none of.
2. What the timeline becomes. A shorter window (four weeks rather than the whole
   book) is a different chart, not a smaller one.
3. Whether the paid-rate strip windows to the last six months, scrolls
   horizontally, or is dropped from the phone layout.
4. Where the theme toggles live when the nav is a drawer that is closed by
   default.
5. Whether bulk selection survives. The sticky action bar works at any width;
   a per-row checkbox column is what does not.

## What is already true and should not be redone

- Every colour is a token, so the four themes work at any width already.
- The drawer, the backdrop and their z-index ladder are correct and were
  re-tested during the desktop pass.
- No layout below 1240px is load-bearing for any test.
```

- [ ] **Step 10: Commit**

```bash
git add bills-frontend/BillsFrontEndBlazor/wwwroot/css/tokens.css bills-frontend/BillsFrontEndBlazor/wwwroot/css/site.css bills-frontend/BillsFrontEndBlazor/Shared/MainLayout.razor bills-frontend/BillsFrontEndBlazor/Shared/MainLayout.razor.css bills-frontend/BillsFrontEndBlazor/Shared/NavMenu.razor.css docs/mobile-layout-follow-up.md
git commit -m "Re-skin the shell and delete the stylesheet the rebuild orphaned"
```
