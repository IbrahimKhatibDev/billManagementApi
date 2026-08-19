using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BillsMinimalApi.Auth;

/// <summary>
/// Who is making this request, as far as the data layer is concerned.
/// <para>
/// <see cref="Data.AppDbContext"/> needs the caller's id to apply its ownership
/// query filter, but a DbContext that reaches for <c>IHttpContextAccessor</c>
/// cannot be constructed outside a request — which is exactly where the seeder,
/// the design-time migration factory and any future background job live. One
/// small interface keeps the context ignorant of HTTP and gives those three a
/// straightforward way to say "nobody" or "the demo account".
/// </para>
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// The authenticated user's id, or <c>null</c> when the request is
    /// anonymous. Null is a meaningful value here, not a missing one: the query
    /// filter compares it against a non-nullable column, so an anonymous caller
    /// matches no bills rather than all of them.
    /// </summary>
    string? Id { get; }
}

/// <summary>
/// Reads the id out of the bearer token's <c>sub</c> claim.
/// </summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    // "sub" rather than ClaimTypes.NameIdentifier because Program.cs turns off
    // MapInboundClaims. The default mapping rewrites the JWT's registered claim
    // names into the long-form SOAP-era URIs, so the claim this reads would
    // otherwise be named nothing like what the token actually carries.
    public string? Id =>
        _accessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
}

/// <summary>
/// Nobody. Used by the seeder and by the design-time factory, both of which run
/// with no request in flight.
/// </summary>
public sealed class NoCurrentUser : ICurrentUser
{
    public string? Id => null;
}
