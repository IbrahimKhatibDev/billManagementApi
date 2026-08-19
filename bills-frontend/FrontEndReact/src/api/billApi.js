import { api } from "./client";

// Every call below goes through the shared instance in client.js, which is what
// attaches the bearer token. Bills are scoped to their owner server-side, so an
// unauthenticated request here does not return everyone's bills — it returns 401.
const BILLS = "/restapi/BillDtos";

// GET ONE PAGE OF BILLS
//
// Not "get all" any more: the endpoint pages, and the response body is
// { items, page, pageSize, totalCount, totalPages, firstRowNumber,
//   lastRowNumber, hasPrevious, hasNext } rather than a bare array.
//
// Searching, filtering and sorting are query-string parameters because the
// database does them. Asking for everything and narrowing it here would work
// only until the table outgrew one page, and then it would quietly start
// searching the first ten rows instead of all of them.
//
// pageSize is clamped server-side (max 100), so a larger number is not an
// error — it just does not get you the whole table.
export async function getBills({
  page = 1,
  pageSize = 10,
  search = "",
  status = "all",
  sort = "id",
  dir = "asc",
} = {}) {
  const params = new URLSearchParams({ page, pageSize, sort, dir });

  // Omitted rather than sent empty: the API treats a blank search as "no
  // search", and leaving them off keeps the URL readable in the network tab.
  if (search.trim()) {
    params.set("search", search.trim());
  }

  if (status !== "all") {
    params.set("status", status);
  }

  return api.get(`${BILLS}?${params}`);
}

// GET BY ID
export async function getBill(id) {
  return api.get(`${BILLS}/${id}`);
}

// CREATE BILL
export async function createBill(bill) {
  return api.post(BILLS, bill, {
    headers: {
      "Content-Type": "application/json",
    },
  });
}

// UPDATE BILL
export async function updateBill(id, bill) {
  return api.put(`${BILLS}/${id}`, bill, {
    headers: {
      "Content-Type": "application/json",
    },
  });
}

// DELETE BILL
export async function deleteBill(id) {
  return api.delete(`${BILLS}/${id}`);
}
