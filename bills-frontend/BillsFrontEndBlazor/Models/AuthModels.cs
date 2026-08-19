namespace BillsFrontEndBlazor.Models
{
    /// <summary>
    /// What <c>POST /auth/login</c> and <c>POST /auth/register</c> hand back.
    /// <para>
    /// Declared here rather than reused from the API's own <c>AuthResponse</c>
    /// for the same reason <see cref="Bill"/> is: this project references
    /// BillsMinimalApi.Contracts and nothing else, so taking a dependency on the
    /// API project would drag EF Core, Npgsql and Swashbuckle into the front end
    /// to share three properties. The API's copy is the authority; if these
    /// drift, deserialisation leaves the token empty and login fails loudly.
    /// </para>
    /// </summary>
    public class AuthResponse
    {
        public string Token { get; set; } = string.Empty;

        public DateTime ExpiresAtUtc { get; set; }

        public string Email { get; set; } = string.Empty;
    }

    /// <summary>
    /// Outcome of a login or registration attempt.
    /// <para>
    /// Failures carry a list rather than a single string because registration
    /// genuinely produces several at once — Identity answers "passwords must
    /// have at least one digit" and "email is already taken" together, and
    /// showing only the first sends the user round the loop twice.
    /// </para>
    /// </summary>
    public sealed record AuthResult(AuthResponse? Auth, IReadOnlyList<string> Errors)
    {
        public bool Succeeded => Auth is not null;

        public static AuthResult Ok(AuthResponse auth) => new(auth, Array.Empty<string>());

        public static AuthResult Failed(params string[] errors) => new(null, errors);

        public static AuthResult Failed(IReadOnlyList<string> errors) => new(null, errors);
    }
}
