using System;

namespace BillsMinimalApi.Models
{
    public class Bill
    {
        public long Id { get; set; }

        /// <summary>
        /// The <see cref="AppUser"/> this bill belongs to. Server-owned: it is
        /// stamped from the bearer token in
        /// <c>AppDbContext.StampAuditFields</c>, never accepted from a client,
        /// and never changed after the insert. It is absent from
        /// <c>BillDto</c> for the same reason.
        /// </summary>
        public string OwnerId { get; set; } = string.Empty;

        public string PayeeName { get; set; } = string.Empty;

        public DateTime DueDate { get; set; }

        public decimal PaymentDue { get; set; }

        public bool Paid { get; set; }

        public int Version { get; set; }

        public DateTime CreateTime { get; set; }

        public DateTime? UpdateTime { get; set; }
    }
}
