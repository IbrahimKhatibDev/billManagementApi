import axios from "axios";
import { clearSession, getToken } from "./session";

// Vite inlines VITE_* variables at build time, so this is fixed once the bundle
// is built — see .env.example. The fallback is where the API listens by default
// both under `dotnet run` and under Docker Compose.
//
// It has to be an absolute URL the *browser* can reach: unlike the Blazor app,
// this is a static SPA, so every request comes from the browser rather than from
// a server, and http://api:8080 would only resolve inside the compose network.
//
// This is the API root, not the bills collection. It was the collection until
// there was a second family of endpoints (/auth/*) to reach as well.
export const API_ROOT = (
  import.meta.env.VITE_API_BASE_URL || "http://localhost:5131"
).replace(/\/+$/, "");

// The one client that carries the bearer token. Sign-in requests use bare axios
// instead — see authApi.js.
export const api = axios.create({
  baseURL: API_ROOT,
  headers: { Accept: "application/json" },
});

// Read at request time rather than baked in at startup: the token arrives after
// this module is first evaluated, and it can be cleared while the app is running.
api.interceptors.request.use((config) => {
  const token = getToken();

  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }

  return config;
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    // A 401 means the token the API was given is no longer good — expired, or
    // issued under a signing key the API has since been restarted with. Dropping
    // it here is what turns that into the login form instead of every request on
    // the page failing in the same way forever.
    if (error.response?.status === 401) {
      clearSession();
    }

    return Promise.reject(error);
  }
);
