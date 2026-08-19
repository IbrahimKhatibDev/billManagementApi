using BillsMinimalApi.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BillsMinimalApi.Auth;

/// <summary>
/// Issues the bearer tokens that <c>/auth/login</c> hands out.
/// </summary>
public sealed class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public (string Token, DateTime ExpiresAtUtc) Create(AppUser user)
    {
        // Truncated to whole seconds: "exp" is a NumericDate, so the fractional
        // part is dropped on the way into the token. Truncating here means the
        // expiry reported to the client is the one actually encoded, rather than
        // up to a second later than it.
        var now = DateTime.UtcNow;
        var expires = new DateTime(
                now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond),
                DateTimeKind.Utc)
            .AddMinutes(_options.LifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Expires = expires,
            Claims = new Dictionary<string, object>
            {
                // The id, not the email: it is what Bill.OwnerId stores, and it
                // is the one thing about an account that never changes.
                [JwtRegisteredClaimNames.Sub] = user.Id,
                [JwtRegisteredClaimNames.Email] = user.Email ?? string.Empty,
            },
            SigningCredentials = new SigningCredentials(
                _options.SecurityKey(),
                SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
