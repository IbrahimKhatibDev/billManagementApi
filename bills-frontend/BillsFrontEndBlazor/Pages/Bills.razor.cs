using BillsFrontEndBlazor.Models;

namespace BillsFrontEndBlazor.Pages
{
    public partial class Bills
    {
        // add bill
        private List<Bill> BillList = new();
        private bool ShowCreateModal = false;

        // edit bill
        private bool ShowEditModal = false;
        private Bill _editBill = new Bill();

        // delete bill
        private bool ShowDeleteModal = false;
        private Bill _deleteBill = new Bill();

        // search
        private string SearchText = string.Empty;

        // Alerts
        private string AlertMessage = string.Empty;
        private bool IsError = false;


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
            var success = await BillService.CreateBillAsync(_newBill);

            if (success)
            {
                ShowSuccess("Bill created successfully.");
                ShowCreateModal = false;
                await LoadBills();
            }
            else
            {
                ShowError("Failed to create bill. Please try again.");
            }
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

        private async Task SaveEditedBill()
        {
            var success = await BillService.UpdateBillAsync(_editBill);

            if (success)
            {
                ShowSuccess("Bill updated successfully.");
                ShowEditModal = false;
                await LoadBills();
            }
            else
            {
                ShowError("Failed to update bill.");
            }
        }

        private void OpenDeleteModal(Bill bill)
        {
            _deleteBill = bill;
            ShowDeleteModal = true;
        }

        private void CloseDeleteModal()
        {
            ShowDeleteModal = false;
        }

        private async Task ConfirmDelete()
        {
            var success = await BillService.DeleteBillAsync(_deleteBill.Id);

            if (success)
            {
                ShowSuccess("Bill deleted successfully.");
                ShowDeleteModal = false;
                await LoadBills();
            }
            else
            {
                ShowError("Failed to delete bill.");
            }
        }

        private IEnumerable<Bill> FilteredBills =>
            string.IsNullOrWhiteSpace(SearchText)
                ? BillList
                : BillList.Where(b =>
                    // match ID if numeric
                    b.Id.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    ||
                    // match PayeeName
                    (b.PayeeName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false)
                );

        private void ShowSuccess(string message)
        {
            AlertMessage = message;
            IsError = false;
        }

        private void ShowError(string message)
        {
            AlertMessage = message;
            IsError = true;
        }

    }

}

