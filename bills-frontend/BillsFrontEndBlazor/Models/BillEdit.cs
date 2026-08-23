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
