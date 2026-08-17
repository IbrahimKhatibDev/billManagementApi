using System.Globalization;
using System.Text;
using BillsFrontEndBlazor.Models;

namespace BillsFrontEndBlazor.Services
{
    /// <summary>
    /// Renders bills as RFC 4180 CSV for the Reports page's export link.
    /// </summary>
    public static class BillCsvWriter
    {
        private static readonly string[] Header =
        {
            "Id", "Payee", "Due date", "Amount", "Status", "Days late",
        };

        public static string Write(IEnumerable<Bill> bills, DateTime today)
        {
            var csv = new StringBuilder();

            AppendRow(csv, Header);

            foreach (var bill in bills)
            {
                var daysLate = DaysLate(bill, today);

                AppendRow(csv, new[]
                {
                    bill.Id.ToString(CultureInfo.InvariantCulture),
                    bill.PayeeName ?? string.Empty,

                    // ISO, not "MMM d, yyyy": this file is going into a
                    // spreadsheet, where 2026-03-15 sorts and parses and
                    // 3/15/2026 depends on the reader's locale.
                    bill.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,

                    // A bare number for the same reason — "$1,234.56" lands in
                    // Excel as text and will not add up.
                    bill.PaymentDue.ToString("0.00", CultureInfo.InvariantCulture),

                    Status(bill, today),
                    daysLate.ToString(CultureInfo.InvariantCulture),
                });
            }

            return csv.ToString();
        }

        private static string Status(Bill bill, DateTime today)
        {
            if (bill.Paid)
            {
                return "Paid";
            }

            return bill.DueDate is { } due && due.Date < today.Date ? "Overdue" : "Unpaid";
        }

        private static int DaysLate(Bill bill, DateTime today)
        {
            if (bill.Paid || bill.DueDate is not { } due)
            {
                return 0;
            }

            return Math.Max(0, (today.Date - due.Date).Days);
        }

        private static void AppendRow(StringBuilder csv, IReadOnlyList<string> fields)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                {
                    csv.Append(',');
                }

                csv.Append(Escape(fields[i]));
            }

            // CRLF is what RFC 4180 specifies, and it is what keeps the file
            // readable in the Windows tools most likely to open it.
            csv.Append("\r\n");
        }

        /// <summary>
        /// Payee names are free text and routinely contain commas — "Sanford,
        /// Turcotte and Farrell" unquoted would silently shift every later
        /// column of that row by one.
        /// </summary>
        private static string Escape(string field)
        {
            var needsQuotes = field.Contains(',')
                              || field.Contains('"')
                              || field.Contains('\n')
                              || field.Contains('\r');

            if (!needsQuotes)
            {
                return field;
            }

            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
    }
}
