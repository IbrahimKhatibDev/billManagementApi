import axios from "axios";

const API_BASE_URL = "https://localhost:7161/restapi/BillDtos";

// GET ALL BILLS
export async function getBills() {
  return axios.get(API_BASE_URL, {
    headers: {
      Accept: "application/json",
    },
  });
}

// GET BY ID
export async function getBill(id) {
  return axios.get(`${API_BASE_URL}/${id}`, {
    headers: {
      Accept: "application/json",
    },
  });
}

// CREATE BILL
export async function createBill(bill) {
  return axios.post(API_BASE_URL, bill, {
    headers: {
      "Content-Type": "application/json",
    },
  });
}

// UPDATE BILL
export async function updateBill(id, bill) {
  return axios.put(`${API_BASE_URL}/${id}`, bill, {
    headers: {
      "Content-Type": "application/json",
    },
  });
}

// DELETE BILL
export async function deleteBill(id) {
  return axios.delete(`${API_BASE_URL}/${id}`);
}
