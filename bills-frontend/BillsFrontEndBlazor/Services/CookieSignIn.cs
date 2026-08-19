using System.Security.Claims;
using BillsFrontEndBlazor.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BillsFrontEndBlazor.Services
{
    /// <summary>
    /// Turns a successful <c>/auth</c> response into a sign-in cookie.
    /// <para>
    /// Shared by the login and register pages, which both end the same way: the
    /// API hands back a token, and the browser needs something it can present on
    /// the next request.
    /// </para>
    /// </summary>
    public static class CookieSignIn
    {
        public static Task SignInAsync(HttpContext http, AuthResponse auth)
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, auth.Email),

                    // The API token. It never leaves the server: the cookie is
                    // encrypted with Data Protection and marked HttpOnly, so what
                    // the browser holds is opaque to it and unreadable from
                    // script.
                    new Claim(ApiTokenAccessor.TokenClaim, auth.Token),
                },
                CookieAuthenticationDefaults.AuthenticationScheme,
                nameType: ClaimTypes.Name,
                roleType: ClaimTypes.Role);

            var properties = new AuthenticationProperties
            {
                // Survives closing the browser, which is only a real decision
                // because of the line below: the cookie cannot outlive the token
                // anyway, so "persistent" buys at most the remainder of an hour.
                IsPersistent = true,

                // The whole reason the cookie and the token stay in step.
                // SpecifyKind rather than trusting the deserialiser: if this
                // arrived as Unspecified, DateTimeOffset would read it as local
                // time and the cookie would expire hours early or late depending
                // on where the server is.
                ExpiresUtc = new DateTimeOffset(
                    DateTime.SpecifyKind(auth.ExpiresAtUtc, DateTimeKind.Utc)),
            };

            return http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                properties);
        }
    }
}
