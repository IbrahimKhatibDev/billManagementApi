namespace BillsMinimalApi.Contracts;

/// <summary>
/// How a batch of deletes went, and how to say so.
/// <para>
/// A sibling of <see cref="BulkPaidOutcome"/> rather than one type the two share
/// by passing a verb in. The counting is identical and the sentences are not:
/// "could not be saved" is the wrong second half of "could not be deleted", and
/// a bill that has been deleted is gone rather than changed. Two short records
/// that each read as English beat one that reads as a template.
/// </para>
/// </summary>
public readonly record struct BulkDeleteOutcome(int Succeeded, int Failed)
{
    public int Total => Succeeded + Failed;

    /// <summary>
    /// Something was deleted and nothing was refused. Zero of each is not
    /// success — it is a batch that never happened.
    /// </summary>
    public bool AllSucceeded => Failed == 0 && Succeeded > 0;

    /// <param name="reason">
    /// Why the batch was not clean — a whole sentence, appended after the
    /// headline. Null or empty appends nothing.
    /// </param>
    public string Describe(string? reason = null)
    {
        var headline = (Succeeded, Failed) switch
        {
            (0, 0) => "Nothing to delete",
            (_, 0) => $"Deleted {Succeeded} {(Succeeded == 1 ? "bill" : "bills")}",
            (0, 1) => "Could not delete that bill",
            (0, _) => $"Could not delete any of those {Failed} bills",

            // Both numbers, always — and more pointedly here than for a paid
            // batch. The deletes that landed are gone for good, so a message
            // that reported only the failure would leave you believing the
            // whole selection survived.
            _ => $"Deleted {Succeeded} of {Total} — {Failed} could not be deleted",
        };

        return string.IsNullOrEmpty(reason) ? headline : $"{headline}. {reason}";
    }
}
