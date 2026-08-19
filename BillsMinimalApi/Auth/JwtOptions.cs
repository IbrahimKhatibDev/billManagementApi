using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BillsMinimalApi.Auth;

/// <summary>
/// Bound from the "Jwt" configuration section.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Minimum length of <see cref="SigningKey"/>, in bytes. HMAC-SHA256 needs a
    /// key at least as long as its output, and the token handler enforces it —
    /// but it does so at the first sign-in, with an exception about key sizes.
    /// Checking at startup turns that into a configuration error nobody has to
    /// decode.
    /// </summary>
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; set; } = "BillsMinimalApi";

    public string Audience { get; set; } = "BillsMinimalApi";

    /// <summary>
    /// The HMAC secret. Deliberately empty in appsettings.json: a signing key
    /// committed to a public repository is a signing key everybody has. Supply
    /// it as <c>Jwt__SigningKey</c>; in Development, Program.cs generates a
    /// throwaway one at startup if it is missing.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public int LifetimeMinutes { get; set; } = 60;

    public SymmetricSecurityKey SecurityKey() =>
        new(Encoding.UTF8.GetBytes(SigningKey));
}
