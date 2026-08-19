import { useEffect, useState } from "react";
import Bills from "./Bills";
import SignIn from "./SignIn";
import { clearSession, getEmail, getToken, subscribe } from "./api/session";
import "./App.css";

// The gate. Bills are per-owner at the API now, so without a token there is
// nothing to show — not an empty table, a 401.
function App() {
  const [session, setSession] = useState(readSession);

  // The session changes from three places: signing in, signing out, and the 401
  // interceptor in client.js deciding the token is spent. All three go through
  // the store, so this is the only listener any of them needs.
  useEffect(() => subscribe(() => setSession(readSession())), []);

  if (!session.token) {
    return <SignIn />;
  }

  return (
    <div className="app-container">
      <div className="app-header">
        <h1>Bills</h1>

        <div className="app-account">
          <span className="muted">{session.email}</span>
          {/* Nothing to tell the API: the token it issued is stateless and
              stays valid until it expires. Forgetting it here is what ends the
              session as far as this browser is concerned. */}
          <button onClick={clearSession}>Sign out</button>
        </div>
      </div>

      <Bills />
    </div>
  );
}

function readSession() {
  return { token: getToken(), email: getEmail() };
}

export default App;
