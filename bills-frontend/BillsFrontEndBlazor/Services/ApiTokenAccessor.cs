using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace BillsFrontEndBlazor.Services
{
    /// <summary>
    /// Finds the API bearer token belonging to whoever this call is being made
    /// on behalf of.
    /// <para>
    /// The token is carried as a claim inside the sign-in cookie, so "who is
    /// signed in" and "which token to send" are answered by the same thing and
    /// cannot disagree. The cookie is encrypted by Data Protection and marked
    /// HttpOnly, so the token never reaches the browser as script-readable
    /// state.
    /// </para>
    /// <para>
    /// The two lookups exist because this app renders in two different worlds. A
    /// live Blazor circuit has no <see cref="HttpContext"/> — the user is on the
    /// SignalR connection, and <see cref="AuthenticationStateProvider"/> is the
    /// only thing that knows them. The prerender pass and the
    /// <c>/reports/bills.csv</c> endpoint are ordinary HTTP requests with no
    /// circuit, where the reverse is true and
    /// <c>GetAuthenticationStateAsync</c> throws.
    /// </para>
    /// <para>
    /// A <see cref="DelegatingHandler"/> on the typed client would have been the
    /// tidier place for this and does not work:
    /// <see cref="IHttpClientFactory"/> builds handler chains in a scope of its
    /// own, so a handler injecting a scoped circuit service gets a different
    /// scope than the circuit it is serving. The typed client itself is
    /// activated from the calling scope, so constructor injection into
    /// <see cref="BillService"/> does resolve correctly.
    /// </para>
    /// </summary>
    public sealed class ApiTokenAccessor
    {
        /// <summary>
        /// Claim type the JWT travels in. A private name rather than one of the
        /// registered ones: it is this app's own bookkeeping, and reusing a
        /// standard claim type would invite something else to interpret it.
        /// </summary>
        public const string TokenClaim = "bills:api_token";

        private readonly IHttpContextAccessor _httpContext;
        private readonly AuthenticationStateProvider _authState;

        public ApiTokenAccessor(
            IHttpContextAccessor httpContext,
            AuthenticationStateProvider authState)
        {
            _httpContext = httpContext;
            _authState = authState;
        }

        public async ValueTask<string?> GetTokenAsync()
        {
            // An HTTP request in flight: prerender, or the CSV endpoint. Checked
            // first because it is the only one of the two that is ever true
            // outside a circuit, and it is authoritative when it is.
            if (_httpContext.HttpContext?.User is { Identity.IsAuthenticated: true } user)
            {
                return TokenOf(user);
            }

            try
            {
                var state = await _authState.GetAuthenticationStateAsync();
                return TokenOf(state.User);
            }
            catch (InvalidOperationException)
            {
                // ServerAuthenticationStateProvider throws rather than returning
                // an anonymous user when nothing has set the state — which is the
                // case in any scope that is neither a circuit nor a request. No
                // token to find, which is not an error worth propagating.
                return null;
            }
        }

        private static string? TokenOf(ClaimsPrincipal user) =>
            user.FindFirst(TokenClaim)?.Value;
    }
}
