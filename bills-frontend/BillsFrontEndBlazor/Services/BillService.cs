using System.Net.Http.Json;
using BillsFrontEndBlazor.Models;

namespace BillsFrontEndBlazor.Services
{
    public class BillService
    {
        private readonly HttpClient _http;

        public BillService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<Bill>> GetBillsAsync()
            => await _http.GetFromJsonAsync<List<Bill>>("restapi/BillDtos")
               ?? new List<Bill>();

        public async Task<bool> CreateBillAsync(Bill bill)
        {
            var result = await _http.PostAsJsonAsync("restapi/BillDtos", bill);
            return result.IsSuccessStatusCode;
        }

        public async Task<bool> UpdateBillAsync(Bill bill)
        {
            var result = await _http.PutAsJsonAsync($"restapi/BillDtos/{bill.Id}", bill);
            return result.IsSuccessStatusCode;
        }

        public async Task<bool> DeleteBillAsync(int id)
        {
            var result = await _http.DeleteAsync($"restapi/BillDtos/{id}");
            return result.IsSuccessStatusCode;
        }
    }
}