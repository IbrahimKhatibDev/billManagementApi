using System.ComponentModel.DataAnnotations;
using BillsFrontEndBlazor.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BillsFrontEndBlazor.Pages.Account
{
    /// <summary>
    /// Exchanges an email and password for a sign-in cookie.
    /// <para>
    /// A Razor Page rather than a Blazor component, and a plain form post rather
    /// than an <c>EditForm</c>, because the outcome is a <c>Set-Cookie</c>
    /// header. A Blazor circuit has already sent its response headers by the time
    /// any component code runs, so signing in from one is not possible —
    /// <c>HttpContext.SignInAsync</c> there throws, or silently does nothing.
    /// </para>
    /// </summary>
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        // Repeated from the API's DbSeeder rather than referenced: this project
        // deliberately does not reference the API project. If the seeder's
        // credentials change, this hint goes stale — which is why it is only a
        // hint, and why the sign-in below has no special case for it.
        public const string DemoEmail = "demo@billsapp.dev";
        public const string DemoPassword = "Demo12345";

        private readonly ApiAuthClient _auth;

        public LoginModel(ApiAuthClient auth)
        {
            _auth = auth;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        /// <summary>Whatever the API said went wrong. Empty on a first visit.</summary>
        public IReadOnlyList<string> Errors { get; private set; } = Array.Empty<string>();

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _auth.LoginAsync(Input.Email, Input.Password, ct);

            if (!result.Succeeded)
            {
                Errors = result.Errors;

                // Blanked so a wrong password is not sitting in the box, in
                // plain text, for whoever walks past next.
                Input.Password = string.Empty;
                ModelState.Remove($"{nameof(Input)}.{nameof(InputModel.Password)}");

                return Page();
            }

            await CookieSignIn.SignInAsync(HttpContext, result.Auth!);

            // Url.IsLocalUrl, not a plain redirect. ReturnUrl arrives in the query
            // string, so without this check anyone could hand out a link to this
            // page that bounces off it to somewhere else — with the credibility
            // of having come from this site, and having just asked for a
            // password.
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "~/");
        }

        public class InputModel
        {
            [Required(ErrorMessage = "Please enter your email address")]
            [EmailAddress(ErrorMessage = "That does not look like an email address")]
            public string Email { get; set; } = string.Empty;

            // No MinLength here, unlike registration. A length rule on sign-in
            // rejects a short password locally with a different message than a
            // wrong one, which tells an attacker something about the account.
            [Required(ErrorMessage = "Please enter your password")]
            public string Password { get; set; } = string.Empty;
        }
    }
}
