namespace BillsMinimalApi.Contracts;

/// <summary>
/// How a batch of mark-paid writes went, and how to say so.
/// <para>
/// In the contracts project rather than beside the service that produces it,
/// because the sentence is the part worth testing and the service is not
/// reachable from a unit test. Named for the operation it describes rather than
/// for the shape it has — see <see cref="BulkDeleteOutcome"/>, which counts the
/// same way and says something else entirely.
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
