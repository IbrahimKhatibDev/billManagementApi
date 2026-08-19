import axios from "axios";
import { API_ROOT } from "./client";
import { saveSession } from "./session";

// The seeded account, so a reviewer can look around without signing up. Repeated
// from the API's DbSeeder rather than fetched: there is no endpoint that hands
// out credentials, and there should not be.
export const DEMO_EMAIL = "demo@billsapp.dev";
export const DEMO_PASSWORD = "Demo12345";

export function login(email, password) {
  return post("/auth/login", email, password, "Could not sign in.");
}

export function register(email, password) {
  return post("/auth/register", email, password, "Could not create the account.");
}

// Bare axios, not the shared instance: this is how a token is obtained, so
// sending a stale one along would be pointless, and the instance's 401
// interceptor would clear the session on a mistyped password.
async function post(route, email, password, fallback) {
  try {
    const response = await axios.post(
      `${API_ROOT}${route}`,
      { email, password },
      { headers: { "Content-Type": "application/json" } }
    );

    saveSession(response.data);
    return { ok: true, errors: [] };
  } catch (err) {
    return { ok: false, errors: messagesFrom(err, fallback) };
  }
}

// The API answers failures as ProblemDetails: a bad password is a `detail`
// string, and Identity's password rules are a `errors` dictionary of arrays.
// Both are worth showing; anything else falls back to the generic line.
function messagesFrom(err, fallback) {
  const problem = err.response?.data;

  if (problem?.errors) {
    const messages = Object.values(problem.errors).flat();
    if (messages.length > 0) return messages;
  }

  if (problem?.detail) return [problem.detail];

  if (!err.response) return ["Could not reach the bills API. Is it running?"];

  return [fallback];
}
