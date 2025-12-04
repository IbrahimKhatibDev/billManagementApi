using System;
using System.ComponentModel.DataAnnotations;

namespace BillsMinimalApi.Dtos
{
    public class BillDto
    {
        public long Id { get; set; }

        [Required(ErrorMessage = "Please enter a payee name")]
        public string PayeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter/select the due date")]
        public DateTime DueDate { get; set; }

        [Required(ErrorMessage = "Please enter the amount that is due.")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Payment due must be positive.")]
        public decimal PaymentDue { get; set; }

        public bool Paid { get; set; }

        public int? Version { get; set; }
    }
}
