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
