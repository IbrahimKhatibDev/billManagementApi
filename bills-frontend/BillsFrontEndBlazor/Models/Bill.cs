namespace BillsFrontEndBlazor.Models
{
    public class Bill
    {
        public int Id { get; set; }
        public string PayeeName { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public decimal PaymentDue { get; set; }
        public bool Paid { get; set; }
        public int Version { get; set; }
    }
}
