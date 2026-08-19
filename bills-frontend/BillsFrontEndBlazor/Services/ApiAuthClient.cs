using System.Net.Http.Json;
using System.Text.Json;
using BillsFrontEndBlazor.Models;

namespace BillsFrontEndBlazor.Services
{
    /// <summary>
    /// Talks to the API's <c>/auth</c> endpoints.
    /// <para>
    /// Deliberately not part of <see cref="BillService"/>, and on its own
    /// <see cref="HttpClient"/>: this is the one client that must send requests
    /// <em>without</em> a bearer token, because it is how the token is obtained.
    /// Folding it into BillService would mean a client that attaches
    /// credentials to the request that exists to establish them.
    /// </para>
    /// </summary>
    public class ApiAuthClient
    {
        private readonly HttpClient _http;

        public ApiAuthClient(HttpClient http)
        {
            _http = http;
        }

        public Task<AuthResult> LoginAsync(
            string email,
            string password,
            CancellationToken ct = default)
            => PostAsync("auth/login", email, password, "Could not sign in.", ct);

        public Task<AuthResult> RegisterAsync(
            string email,
            string password,
            CancellationToken ct = default)
            => PostAsync("auth/register", email, password, "Could not create the account.", ct);

        private async Task<AuthResult> PostAsync(
            string route,
            string email,
            string password,
            string fallback,
            CancellationToken ct)
        {
            HttpResponseMessage response;

            try
            {
                // An anonymous object rather than a shared request type: the two
                // endpoints take the same two fields, and a DTO whose only job is
                // to be serialised once is a type to keep in sync for nothing.
                response = await _http.PostAsJsonAsync(route, new { email, password }, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // The API being down is the single most likely failure the first
                // time somebody runs this, so it gets its own message rather than
                // the generic one.
                return AuthResult.Failed("Could not reach the bills API. Is it running?");
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(ct);

                    return auth is null || string.IsNullOrEmpty(auth.Token)
                        ? AuthResult.Failed(fallback)
                        : AuthResult.Ok(auth);
                }

                return AuthResult.Failed(await ReadProblemAsync(response, fallback, ct));
            }
        }

        /// <summary>
        /// Turns the API's error response into something a person can read.
        /// <para>
        /// Two shapes arrive here. <c>/auth/login</c> returns a ProblemDetails
        /// with the message in <c>detail</c>; <c>/auth/register</c> returns a
        /// ValidationProblemDetails whose <c>errors</c> map holds one or more
        /// descriptions per key. Both are handled rather than only the second,
        /// because the first is the one users hit every day.
        /// </para>
        /// </summary>
        private static async Task<IReadOnlyList<string>> ReadProblemAsync(
            HttpResponseMessage response,
            string fallback,
            CancellationToken ct)
        {
            Problem? problem = null;

            try
            {
                problem = await response.Content.ReadFromJsonAsync<Problem>(ct);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                // Not a problem document at all — an HTML error page from a proxy,
                // or an empty body. Nothing to salvage; fall through to the
                // fallback rather than surfacing a parse error to the user.
            }

            if (problem?.Errors is { Count: > 0 } errors)
            {
                // The keys are field names and Identity error codes
                // ("DuplicateUserName"), which mean nothing to the person
                // reading them. Only the descriptions are shown.
                return errors.SelectMany(pair => pair.Value).ToArray();
            }

            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return new[] { problem.Detail };
            }

            return new[] { fallback };
        }

        /// <summary>
        /// Enough of ProblemDetails and ValidationProblemDetails to read both.
        /// <para>
        /// Hand-rolled rather than using <c>Microsoft.AspNetCore.Mvc</c>'s types:
        /// <c>ValidationProblemDetails.Errors</c> has no setter that
        /// System.Text.Json can reach, so deserialising into it silently yields
        /// an empty dictionary — a bug that looks exactly like "the server sent
        /// no errors".
        /// </para>
        /// </summary>
        private sealed record Problem(
            string? Title,
            string? Detail,
            Dictionary<string, string[]>? Errors);
    }
}
