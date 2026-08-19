using System.ComponentModel.DataAnnotations;
using BillsFrontEndBlazor.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BillsFrontEndBlazor.Pages.Account
{
    /// <summary>
    /// Creates an account and signs straight into it.
    /// <para>
    /// The API returns a token from <c>POST /auth/register</c> exactly as it does
    /// from login, so there is no reason to make someone type their password a
    /// second time on a screen they just left.
    /// </para>
    /// </summary>
    [AllowAnonymous]
    public class RegisterModel : PageModel
    {
        private readonly ApiAuthClient _auth;

        public RegisterModel(ApiAuthClient auth)
        {
            _auth = auth;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        /// <summary>
        /// Whatever the API refused on — a duplicate email, or Identity's own
        /// password rules. Those live on the server, so they are reported rather
        /// than duplicated here.
        /// </summary>
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

            var result = await _auth.RegisterAsync(Input.Email, Input.Password, ct);

            if (!result.Succeeded)
            {
                Errors = result.Errors;

                Input.Password = string.Empty;
                Input.ConfirmPassword = string.Empty;
                ModelState.Remove($"{nameof(Input)}.{nameof(InputModel.Password)}");
                ModelState.Remove($"{nameof(Input)}.{nameof(InputModel.ConfirmPassword)}");

                return Page();
            }

            await CookieSignIn.SignInAsync(HttpContext, result.Auth!);

            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "~/");
        }

        public class InputModel
        {
            [Required(ErrorMessage = "Please enter your email address")]
            [EmailAddress(ErrorMessage = "That does not look like an email address")]
            public string Email { get; set; } = string.Empty;

            // Deliberately the weakest of the rules the API enforces. Anything
            // stricter here and the two disagree, which shows up as a form that
            // passes locally and is then rejected by the server.
            [Required(ErrorMessage = "Please choose a password")]
            [MinLength(8, ErrorMessage = "Passwords need at least 8 characters")]
            public string Password { get; set; } = string.Empty;

            [Required(ErrorMessage = "Please confirm your password")]
            [Compare(nameof(Password), ErrorMessage = "The two passwords do not match")]
            [Display(Name = "Confirm password")]
            public string ConfirmPassword { get; set; } = string.Empty;
        }
    }
}
