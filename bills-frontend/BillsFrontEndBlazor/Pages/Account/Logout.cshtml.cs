using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BillsFrontEndBlazor.Pages.Account
{
    /// <summary>
    /// Drops the sign-in cookie, and with it the API token it carried.
    /// <para>
    /// Anonymous on purpose: someone whose cookie has already expired should land
    /// on a page that says so, not get bounced to the login screen with a
    /// ReturnUrl pointing back at sign-out.
    /// </para>
    /// </summary>
    [AllowAnonymous]
    public class LogoutModel : PageModel
    {
        public string? Email { get; private set; }

        public void OnGet()
        {
            if (User.Identity is { IsAuthenticated: true })
            {
                Email = User.Identity.Name;
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // There is nothing to tell the API about. The token it issued is
            // self-contained and stateless, so it stays valid until it expires;
            // dropping the cookie is what stops this browser from presenting it.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToPage("Login");
        }
    }
}
