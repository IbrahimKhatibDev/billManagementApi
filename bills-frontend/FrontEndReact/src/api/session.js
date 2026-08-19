// WHERE THE TOKEN LIVES
//
// localStorage, which is readable by any script running on this origin. That is
// the accepted trade for a static SPA: the alternative — an HttpOnly cookie —
// needs a server of its own to set it, which is exactly what this client does
// not have. (The Blazor front end next door does have one, and does use a
// cookie.) Nothing here is a substitute for the API's own checks; the token is a
// claim about who you are, not permission to skip asking.
const TOKEN_KEY = "bills.token";
const EMAIL_KEY = "bills.email";
const EXPIRES_KEY = "bills.expiresAt";

// Subscribers, so signing out in one place re-renders everything. A plain Set
// rather than a context: there is one consumer and it is App.
const listeners = new Set();

export function subscribe(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function notify() {
  for (const listener of listeners) {
    listener();
  }
}

export function saveSession({ token, email, expiresAtUtc }) {
  localStorage.setItem(TOKEN_KEY, token);
  localStorage.setItem(EMAIL_KEY, email ?? "");
  localStorage.setItem(EXPIRES_KEY, expiresAtUtc ?? "");
  notify();
}

export function clearSession() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(EMAIL_KEY);
  localStorage.removeItem(EXPIRES_KEY);
  notify();
}

// Checked locally as a courtesy, so an expired session shows the login form
// rather than a table that fails to load. The API checks the same expiry for
// real; a client that skipped this would be refused, not let in.
export function getToken() {
  const token = localStorage.getItem(TOKEN_KEY);
  if (!token) return null;

  const expiresAt = localStorage.getItem(EXPIRES_KEY);

  // The API serialises ExpiresAtUtc as an unsuffixed DateTime ("2026-08-18T09:00:00Z"
  // or without the Z depending on Kind), so append one if it is missing rather
  // than letting Date parse it as local time and drift by the UTC offset.
  if (expiresAt) {
    const normalised = /[zZ]|[+-]\d{2}:\d{2}$/.test(expiresAt)
      ? expiresAt
      : `${expiresAt}Z`;

    if (Date.parse(normalised) <= Date.now()) {
      clearSession();
      return null;
    }
  }

  return token;
}

export function getEmail() {
  return localStorage.getItem(EMAIL_KEY) || "";
}

export function isSignedIn() {
  return getToken() !== null;
}
