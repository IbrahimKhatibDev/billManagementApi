import { useEffect, useState } from "react";
import {
  createBill,
  getBills,
  updateBill,
  deleteBill,
} from "./api/billApi";
import "./App.css";

function App() {
  const [bills, setBills] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [creating, setCreating] = useState(false);
  const [newBill, setNewBill] = useState({
    payeeName: "",
    dueDate: "",
    paymentDue: "",
    paid: false,
    version: 1,
  });

  const [updating, setUpdating] = useState(false);
  const [editRowId, setEditRowId] = useState(null);
  const [search, setSearch] = useState("");

  useEffect(() => {
    loadBills();
  }, []);

  async function loadBills() {
    try {
      setLoading(true);
      setError("");
      const response = await getBills();
      setBills(response.data);
    } catch
     (err) {
      console.error(err);
      setError("Failed to load bills. Check if the backend is running.");
    } finally {
      setLoading(false);
    }
  }

  async function handleCreateBill(e) {
    e.preventDefault();
    setCreating(true);

    try {
      const billToSend = {
        payeeName: newBill.payeeName,
        dueDate: newBill.dueDate,
        paymentDue: Number(newBill.paymentDue),
        paid: Boolean(newBill.paid),
        version: newBill.version,
      };

      await createBill(billToSend);
      alert("Bill Created!");

      setNewBill({
        payeeName: "",
        dueDate: "",
        paymentDue: "",
        paid: false,
        version: 1,
      });

      loadBills();
    } catch (err) {
      console.error(err);
      alert("Failed to create bill.");
    } finally {
      setCreating(false);
    }
  }

  function handleRowChange(id, key, value) {
    setBills((prev) =>
      prev.map((bill) => (bill.id === id ? { ...bill, [key]: value } : bill))
    );
  }

  async function handleSaveRow(bill) {
    setUpdating(true);
    try {
      await updateBill(bill.id, bill);
      alert("Bill updated!");
      setEditRowId(null);
      loadBills();
    } catch (err) {
      console.error(err);
      alert("Failed to update bill.");
    } finally {
      setUpdating(false);
    }
  }

  async function handleDelete(id) {
    if (!confirm("Delete this bill?")) return;
    try {
      await deleteBill(id);
      alert("Deleted");
      loadBills();
    } catch (err) {
      console.error(err);
      alert("Failed to delete bill.");
    }
  }

  const filteredBills = bills.filter((bill) => {
    if (!search.trim()) return true;
    return (
      bill.id.toString().includes(search) ||
      bill.payeeName.toLowerCase().includes(search.toLowerCase())
    );
  });

  return (
    <div className="app-container">
      <h1>Bills</h1>

      <button className="primary" onClick={loadBills}>
        Refresh
      </button>

      {loading && <p>Loading...</p>}
      {error && <p style={{ color: "red" }}>{error}</p>}

      {!loading && !error && (
        <>
          {/* CREATE */}
          <div className="card">
            <h2>Create New Bill</h2>

            <table className="create-table">
              <thead>
                <tr>
                  <th>Payee</th>
                  <th>Due Date</th>
                  <th>Amount</th>
                  <th>Paid</th>
                  <th>Create</th>
                </tr>
              </thead>

              <tbody>
                <tr>
                  <td>
                    <input
                      type="text"
                      required
                      value={newBill.payeeName}
                      onChange={(e) =>
                        setNewBill({ ...newBill, payeeName: e.target.value })
                      }
                    />
                  </td>

                  <td>
                    <input
                      type="date"
                      required
                      value={newBill.dueDate}
                      onChange={(e) =>
                        setNewBill({ ...newBill, dueDate: e.target.value })
                      }
                    />
                  </td>

                  <td>
                    <input
                      type="number"
                      step="0.01"
                      required
                      value={newBill.paymentDue}
                      onChange={(e) =>
                        setNewBill({
                          ...newBill,
                          paymentDue: parseFloat(e.target.value),
                        })
                      }
                    />
                  </td>

                  <td style={{ textAlign: "center" }}>
                    <input
                      type="checkbox"
                      checked={newBill.paid}
                      onChange={(e) =>
                        setNewBill({ ...newBill, paid: e.target.checked })
                      }
                    />
                  </td>

                  <td>
                    <button onClick={handleCreateBill} disabled={creating}>
                      {creating ? "Creating..." : "Create"}
                    </button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          {/* SEARCH */}
          <div className="card">
            <h2>Find Bill</h2>
            <input
              type="search"
              placeholder="Search by ID or Payee"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>

          {/* TABLE */}
          <div className="card">
            <h2>Results</h2>
            <table>
              <thead>
                <tr>
                  <th>ID</th>
                  <th>Payee</th>
                  <th>Due Date</th>
                  <th>Amount</th>
                  <th>Paid</th>
                  <th>Actions</th>
                </tr>
              </thead>

              <tbody>
                {filteredBills.map((bill) => (
                  <tr
                    key={bill.id}
                    className={editRowId === bill.id ? "editing" : ""}
                  >
                    <td>{bill.id}</td>

                    <td>
                      {editRowId === bill.id ? (
                        <input
                          value={bill.payeeName}
                          onChange={(e) =>
                            handleRowChange(
                              bill.id,
                              "payeeName",
                              e.target.value
                            )
                          }
                        />
                      ) : (
                        bill.payeeName
                      )}
                    </td>

                    <td>
                      {editRowId === bill.id ? (
                        <input
                          type="date"
                          value={bill.dueDate.slice(0, 10)}
                          onChange={(e) =>
                            handleRowChange(bill.id, "dueDate", e.target.value)
                          }
                        />
                      ) : (
                        bill.dueDate.slice(0, 10)
                      )}
                    </td>

                    <td>
                      {editRowId === bill.id ? (
                        <input
                          type="number"
                          step="0.01"
                          value={bill.paymentDue}
                          onChange={(e) =>
                            handleRowChange(
                              bill.id,
                              "paymentDue",
                              parseFloat(e.target.value)
                            )
                          }
                        />
                      ) : (
                        bill.paymentDue
                      )}
                    </td>

                    <td>
                      {editRowId === bill.id ? (
                        <input
                          type="checkbox"
                          checked={bill.paid}
                          onChange={(e) =>
                            handleRowChange(bill.id, "paid", e.target.checked)
                          }
                        />
                      ) : bill.paid ? (
                        "Yes"
                      ) : (
                        "No"
                      )}
                    </td>

                    <td>
                      {editRowId === bill.id ? (
                        <>
                          <button
                            className="primary"
                            onClick={() => handleSaveRow(bill)}
                            disabled={updating}
                          >
                            Save
                          </button>
                          <button onClick={() => setEditRowId(null)}>
                            Cancel
                          </button>
                        </>
                      ) : (
                        <>
                          <button onClick={() => setEditRowId(bill.id)}>
                            Edit
                          </button>
                          <button
                            className="danger"
                            onClick={() => handleDelete(bill.id)}
                            style={{ marginLeft: "0.5rem" }}
                          >
                            Delete
                          </button>
                        </>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}

export default App;
