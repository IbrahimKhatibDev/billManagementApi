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
