using System.ComponentModel.DataAnnotations;

namespace BillsMinimalApi.Dtos
{
    public class RegisterRequest
    {
        [Required(ErrorMessage = "Please enter an email address")]
        [EmailAddress(ErrorMessage = "That does not look like an email address")]
        public string Email { get; set; } = string.Empty;

        // The length floor is repeated in Program.cs as an Identity password
        // option. This one exists to reject the request before it reaches
        // UserManager, so the client gets the same shape of validation error it
        // gets for a bad bill rather than an Identity error list.
        [Required(ErrorMessage = "Please enter a password")]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
        public string Password { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        [Required(ErrorMessage = "Please enter an email address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter a password")]
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// What /auth/register and /auth/login return. No refresh token: this app
    /// has one client per user and a one-hour lifetime, so the honest options
    /// were "log in again" or a second token with its own storage, revocation
    /// and rotation story. Adding the machinery without the need would be the
    /// worse of the two.
    /// </summary>
    public class AuthResponse
    {
        public string Token { get; set; } = string.Empty;

        public DateTime ExpiresAtUtc { get; set; }

        public string Email { get; set; } = string.Empty;
    }
}
