# Frontend – Bills App (React + Vite)

This React app connects to the backend Minimal API to load, create, edit, and delete bills.

## Why I Chose React
I chose React because it is simple, widely used, and easy for building. I've also just used it before for classes as well as personal projects so im familiar with it.

## Operating System
Built and tested on Windows 11.

### Requirements
Node.js 20+
NPM

## Setup and Run Instructions
```
npm install
npm run dev
```
Frontend: http://localhost:5173  
Backend API: https://localhost:7161/restapi/BillDtos

## API Base URL Location
If the backend is running on a different machine or port, update the API URL inside:
```
src/api/billApi.js
```

## API Layer (billApi.js)

Provides all CRUD operations:
```
getBills()  
getBill(id)  
createBill(bill)  
updateBill(id, bill)  
deleteBill(id)
```

## How the Frontend Works

### 1. How do you call a REST API when the page initializes?
```
useEffect(() => { loadBills(); }, []);

async function loadBills() {
  const response = await getBills();
  setBills(response.data);
}
```

### 2. What code fetches and displays all items?
```
<tbody>
  {bills.map(b => (
    <tr key={b.id}>
      <td>{b.id}</td>
      <td>{b.payeeName}</td>
      <td>{b.dueDate.slice(0,10)}</td>
      <td>{b.paymentDue}</td>
      <td>{b.paid ? "Yes" : "No"}</td>
    </tr>
  ))}
</tbody>
```

### 3. How is the new-item form rendered and submitted?
```
<input value={newBill.payeeName}
       onChange={e => setNewBill({...newBill, payeeName: e.target.value})} />

async function handleCreateBill(e) {
  e.preventDefault();
  await createBill(newBill);
  loadBills();
}
```

### 4. How do you find and update an existing item?
```
<button onClick={() => setEditRowId(bill.id)}>Edit</button>

<input value={bill.payeeName}
       onChange={e => handleRowChange(bill.id, "payeeName", e.target.value)} />

async function handleSaveRow(bill) {
  await updateBill(bill.id, bill);
  loadBills();
}
```

### 5. How do you delete an item?

```
async function handleDelete(id) {
  await deleteBill(id);
  loadBills();
}
```

This frontend provides simple CRUD operations with a clean React + Axios structure.
