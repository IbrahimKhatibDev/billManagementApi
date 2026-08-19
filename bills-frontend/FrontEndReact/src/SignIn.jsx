import { useState } from "react";
import {
  DEMO_EMAIL,
  DEMO_PASSWORD,
  login,
  register,
} from "./api/authApi";

// Sign in, or create an account, in one form. A router would be two screens and
// a dependency; this is a toggle and a verb.
//
// Nothing calls back on success: login() and register() write to the session
// store, which notifies App, which re-renders past this component. So there is
// no "signed in" state to keep in sync in two places.
function SignIn() {
  const [mode, setMode] = useState("login");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [errors, setErrors] = useState([]);
  const [busy, setBusy] = useState(false);

  const registering = mode === "register";

  async function handleSubmit(e) {
    e.preventDefault();

    if (registering && password !== confirm) {
      setErrors(["The two passwords do not match"]);
      return;
    }

    setBusy(true);
    setErrors([]);

    const result = registering
      ? await register(email, password)
      : await login(email, password);

    if (!result.ok) {
      setErrors(result.errors);
      // Cleared so a wrong password is not sitting in the box in plain text.
      setPassword("");
      setConfirm("");
    }

    setBusy(false);
  }

  function switchMode() {
    setMode(registering ? "login" : "register");
    setErrors([]);
    setPassword("");
    setConfirm("");
  }

  return (
    <div className="app-container signin-container">
      <h1>Bills</h1>

      <div className="card">
        <h2>{registering ? "Create an account" : "Sign in"}</h2>

        {errors.length > 0 && (
          <ul className="signin-errors">
            {errors.map((message) => (
              <li key={message}>{message}</li>
            ))}
          </ul>
        )}

        <form onSubmit={handleSubmit} className="signin-form">
          <label>
            Email
            <input
              type="email"
              required
              autoComplete="username"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
            />
          </label>

          <label>
            Password
            <input
              type="password"
              required
              autoComplete={registering ? "new-password" : "current-password"}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </label>

          {registering && (
            <label>
              Confirm password
              <input
                type="password"
                required
                autoComplete="new-password"
                value={confirm}
                onChange={(e) => setConfirm(e.target.value)}
              />
            </label>
          )}

          <button type="submit" className="primary" disabled={busy}>
            {busy
              ? "Working..."
              : registering
                ? "Create account"
                : "Sign in"}
          </button>
        </form>

        <p className="muted signin-switch">
          {registering ? "Already have an account?" : "No account yet?"}{" "}
          <button type="button" onClick={switchMode}>
            {registering ? "Sign in" : "Create one"}
          </button>
        </p>

        {/* Only ever a demo. The seeded account exists so someone opening this
            has something to look at without signing up, and its credentials are
            in the README too, so printing them here reveals nothing. */}
        {!registering && (
          <p className="muted signin-demo">
            Just looking? Sign in as <code>{DEMO_EMAIL}</code> /{" "}
            <code>{DEMO_PASSWORD}</code>.
          </p>
        )}
      </div>
    </div>
  );
}

export default SignIn;
