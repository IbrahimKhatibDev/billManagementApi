import { useEffect, useState } from "react";
import {
  createBill,
  getBills,
  updateBill,
  deleteBill,
} from "./api/billApi";

// One page of bills at a time. The old version fetched every row and filtered
// the array in the browser; the list endpoint pages now, so that would have
// silently become "search the first ten bills".
const PAGE_SIZE = 10;

// Long enough to swallow a burst of typing, short enough that the table still
// feels live. Without it every keystroke is a database query.
const SEARCH_DEBOUNCE_MS = 300;

// Split out of App when sign-in arrived, and not merely for tidiness: hooks run
// whatever a component returns, so leaving this inside App and returning the
// login form early would still have fired the load effect below — one 401 per
// visitor before they had typed anything. App only mounts this once there is a
// token to send.
function Bills() {
  const [bills, setBills] = useState([]);
  const [pageInfo, setPageInfo] = useState(null);
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
  const [appliedSearch, setAppliedSearch] = useState("");
  const [page, setPage] = useState(1);

  // Bumped to ask for the current page again after a create, edit or delete —
  // the page and the search have not changed, so nothing else would.
  const [reloadToken, setReloadToken] = useState(0);

  // Typing waits out the debounce before it becomes a request, and always
  // lands back on page one: page 3 of the old results is rarely page 3 of the
  // new ones.
  useEffect(() => {
    const timer = setTimeout(() => {
      setAppliedSearch(search);
      setPage(1);
    }, SEARCH_DEBOUNCE_MS);

    return () => clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    // Requests can land out of order — a slow page 2 arriving after a fast
    // page 3 would put the wrong rows on screen. The cleanup runs before the
    // next effect, so only the newest request is allowed to set state.
    let current = true;

    async function load() {
      try {
        setLoading(true);
        setError("");

        const response = await getBills({
          page,
          pageSize: PAGE_SIZE,
          search: appliedSearch,
        });

        if (!current) return;

        setBills(response.data.items);
        setPageInfo(response.data);

        // The API clamps a page past the end rather than serving an empty one,
        // so deleting the last row on the last page hands back the new last
        // page instead of a blank table. Take its word for which page this is,
        // or the next Previous click would be computed from a page number the
        // server has already overruled.
        if (response.data.page !== page) {
          setPage(response.data.page);
        }
      } catch (err) {
        if (!current) return;
        console.error(err);

        // A 401 has already cleared the session by the time this runs — the
        // interceptor in client.js does it — so App is about to swap this
        // component out for the login form. Saying "check if the backend is
        // running" there would be wrong and, briefly, visible.
        setError(
          err.response?.status === 401
            ? "Your session has ended. Sign in again."
            : "Failed to load bills. Check if the backend is running."
        );
      } finally {
        if (current) setLoading(false);
      }
    }

    load();

    return () => {
      current = false;
    };
  }, [page, appliedSearch, reloadToken]);

  function loadBills() {
    setReloadToken((token) => token + 1);
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

  return (
    <>
      <button className="primary" onClick={loadBills}>
        Refresh
      </button>

      {error && <p style={{ color: "red" }}>{error}</p>}

      {/* Deliberately not gated on `loading`: searching is a round trip now, so
          tearing the form down mid-request would take the focus out of the
          search box on every keystroke. */}
      {!error && (
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
            <div className="results-header">
              <h2>Results</h2>
              {loading && <span className="muted">Loading...</span>}
            </div>
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
                {bills.map((bill) => (
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

            {/* The counts come from the response rather than from bills.length:
                that is one page, and "Showing 1-10 of 10" on a table of 26 is
                exactly the lie paging introduces if you count locally. */}
            {pageInfo && (
              <div className="pager">
                <button
                  onClick={() => setPage(pageInfo.page - 1)}
                  disabled={!pageInfo.hasPrevious || loading}
                >
                  Previous
                </button>

                <span className="muted">
                  {pageInfo.totalCount === 0
                    ? "No bills match"
                    : `Showing ${pageInfo.firstRowNumber}-${pageInfo.lastRowNumber} of ${pageInfo.totalCount}`}
                </span>

                <button
                  onClick={() => setPage(pageInfo.page + 1)}
                  disabled={!pageInfo.hasNext || loading}
                >
                  Next
                </button>
              </div>
            )}
          </div>
        </>
      )}
    </>
  );
}

export default Bills;
