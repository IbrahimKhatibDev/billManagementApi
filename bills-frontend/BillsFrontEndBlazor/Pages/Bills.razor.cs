using BillsFrontEndBlazor.Models;

namespace BillsFrontEndBlazor.Pages
{
    public partial class Bills
    {
        private List<Bill> BillList = new();
        private bool ShowCreateModal = false;

        private bool ShowEditModal = false;
        private Bill _editBill = new Bill();

        private Bill _newBill = new()
        {
            DueDate = DateTime.Today,
            Version = 1
        };

        protected override async Task OnInitializedAsync()
        {
            await LoadBills();
        }

        private async Task LoadBills()
        {
            BillList = await BillService.GetBillsAsync();
        }

        private void OpenModal()
        {
            _newBill = new Bill
            {
                DueDate = DateTime.Today,
                Version = 1
            };
            ShowCreateModal = true;
        }

        private void CloseModal()
        {
            ShowCreateModal = false;
        }

        private async Task SaveNewBill()
        {
            await BillService.CreateBillAsync(_newBill);
            ShowCreateModal = false;
            await LoadBills();
        }

        private void OpenEditModal(Bill bill)
        {
            _editBill = new Bill
            {
                Id = bill.Id,
                PayeeName = bill.PayeeName,
                PaymentDue = bill.PaymentDue,
                DueDate = bill.DueDate,
                Paid = bill.Paid,
                Version = bill.Version
            };

            ShowEditModal = true;
        }

        private void CloseEditModal()
        {
            ShowEditModal = false;
        }
    }
}
