namespace BillsFrontEndBlazor.Services
{
    public class BillEventService
    {
        public event Action? OnBillsChanged;

        public void NotifyBillsChanged()
        {
            OnBillsChanged?.Invoke();
        }
    }
}
