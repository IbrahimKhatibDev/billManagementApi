using System.ComponentModel.DataAnnotations;

namespace BillsFrontEndBlazor.Models
{
    public class Bill
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Payee name is required.")]
        public string PayeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Due date is required.")]
        public DateTime? DueDate { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal PaymentDue { get; set; }

        public bool Paid { get; set; }

        public int Version { get; set; }
    }
}
