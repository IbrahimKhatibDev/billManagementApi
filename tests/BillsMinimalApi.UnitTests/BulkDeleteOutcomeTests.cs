using BillsMinimalApi.Contracts;

namespace BillsMinimalApi.UnitTests;

/// <summary>
/// What the app says after deleting a batch of bills.
/// <para>
/// The same reasons <see cref="BulkPaidOutcomeTests"/> gives, with more riding
/// on them: a partial delete cannot be re-run to tidy up, so the sentence is the
/// only record of which half of the selection is gone.
/// </para>
/// </summary>
public sealed class BulkDeleteOutcomeTests
{
    [Fact]
    public void One_bill_is_singular()
    {
        Assert.Equal("Deleted 1 bill", new BulkDeleteOutcome(1, 0).Describe());
    }

    [Fact]
    public void Several_bills_are_plural()
    {
        Assert.Equal("Deleted 3 bills", new BulkDeleteOutcome(3, 0).Describe());
    }

    [Fact]
    public void A_partial_batch_reports_both_halves()
    {
        // The successes are irreversible. A message that named only the failure
        // would leave the reader believing all three bills survived.
        Assert.Equal(
            "Deleted 2 of 3 — 1 could not be deleted",
            new BulkDeleteOutcome(2, 1).Describe());
    }

    [Fact]
    public void A_batch_that_wholly_failed_does_not_claim_a_partial_success()
    {
        Assert.Equal(
            "Could not delete that bill",
            new BulkDeleteOutcome(0, 1).Describe());

        Assert.Equal(
            "Could not delete any of those 4 bills",
            new BulkDeleteOutcome(0, 4).Describe());
    }

    [Fact]
    public void An_empty_batch_does_not_congratulate_itself()
    {
        var outcome = new BulkDeleteOutcome(0, 0);

        Assert.Equal("Nothing to delete", outcome.Describe());
        Assert.False(outcome.AllSucceeded);
    }

    [Fact]
    public void A_reason_is_appended_as_its_own_sentence()
    {
        Assert.Equal(
            "Deleted 2 of 3 — 1 could not be deleted. They were already gone.",
            new BulkDeleteOutcome(2, 1).Describe("They were already gone."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_reason_leaves_no_dangling_punctuation(string? reason)
    {
        Assert.Equal("Deleted 1 bill", new BulkDeleteOutcome(1, 0).Describe(reason));
    }

    [Fact]
    public void All_succeeded_means_something_succeeded_and_nothing_failed()
    {
        Assert.True(new BulkDeleteOutcome(2, 0).AllSucceeded);
        Assert.False(new BulkDeleteOutcome(2, 1).AllSucceeded);
        Assert.False(new BulkDeleteOutcome(0, 0).AllSucceeded);
    }

    [Fact]
    public void Total_counts_everything_that_was_attempted()
    {
        Assert.Equal(5, new BulkDeleteOutcome(3, 2).Total);
    }
}
