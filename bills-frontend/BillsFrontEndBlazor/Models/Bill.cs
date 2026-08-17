using System.ComponentModel.DataAnnotations;

namespace BillsFrontEndBlazor.Models
{
    public class Bill
    {
        // long, not int: the API models Id as long, and any value above
        // int.MaxValue would otherwise fail to deserialize — taking the whole
        // list request down with it, not just the one row.
        public long Id { get; set; }

        [Required(ErrorMessage = "Payee name is required.")]
        public string PayeeName { get; set; } = string.Empty;

        // Nullable on purpose: [Required] on a non-nullable DateTime can never
        // fail, because an unset value is still a valid DateTime. The
        // nullability is what makes client-side validation work at all.
        [Required(ErrorMessage = "Due date is required.")]
        public DateTime? DueDate { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal PaymentDue { get; set; }

        public bool Paid { get; set; }

        public int Version { get; set; }
    }
}
