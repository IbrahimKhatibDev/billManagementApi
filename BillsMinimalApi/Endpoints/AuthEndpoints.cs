using System.Security.Cryptography;
using BillsMinimalApi.Auth;
using BillsMinimalApi.Dtos;
using BillsMinimalApi.Models;
using BillsMinimalApi.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace BillsMinimalApi.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // No RequireAuthorization on this group, for the obvious reason. It is
        // the only group without it — everything else is locked in Program.cs by
        // the fallback policy, so a new endpoint is private unless somebody says
        // otherwise, rather than public unless somebody remembers.
        var group = app.MapGroup("/auth")
                       .WithTags("Auth")
                       .AllowAnonymous();

        // REGISTER
        //
        // Returns a token rather than just a 201: the client's next move after
        // registering is always to log in, and making it do that round trip
        // twice buys nothing.
        group.MapPost("/register", async (
            RegisterRequest request,
            UserManager<AppUser> users,
            JwtTokenService tokens) =>
        {
            var user = new AppUser
            {
                UserName = request.Email,
                Email = request.Email,
            };

            var result = await users.CreateAsync(user, request.Password);

            if (!result.Succeeded)
            {
                // Identity's errors are the useful ones here — "passwords must
                // have at least one digit", "email is already taken" — and they
                // arrive as a list, so they go back as a validation problem
                // rather than being flattened into one string.
                return Results.ValidationProblem(result.Errors
                    .GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
            }

            return Results.Ok(Issue(user, tokens));
        })
        .RequireRateLimiting(RateLimitSetup.AuthPolicy);

        // LOGIN
        group.MapPost("/login", async (
            LoginRequest request,
            UserManager<AppUser> users,
            JwtTokenService tokens) =>
        {
            var user = await users.FindByEmailAsync(request.Email);

            // One message for every way this can fail — no such account, wrong
            // password, account locked — on purpose. Answering them separately
            // turns this endpoint into a way to ask whether a given person has an
            // account here.
            //
            // CheckPasswordAsync rather than SignInManager: there are no cookies
            // in this API, and SignInManager drags in an authentication scheme
            // that would exist only to be unused. The lockout bookkeeping it
            // would have done is done explicitly below instead.
            if (user is null || await users.IsLockedOutAsync(user))
            {
                // The lockout check comes first so a locked account cannot be
                // used as free password-hashing work, which is the other thing
                // repeated sign-in attempts are good for.
                PretendToCheckAPassword(users.PasswordHasher, request.Password);

                return Unauthorized();
            }

            if (!await users.CheckPasswordAsync(user, request.Password))
            {
                // What turns the identical message above into an identical
                // *answer*: five failures and the account stops responding to
                // guesses for a while, whoever is making them and from wherever.
                await users.AccessFailedAsync(user);

                return Unauthorized();
            }

            if (await users.GetAccessFailedCountAsync(user) > 0)
            {
                // A successful sign-in is the evidence that the failures before it
                // were the owner fumbling, not somebody guessing. Guarded because
                // the overwhelming majority of logins have nothing to reset and
                // this is a database write.
                await users.ResetAccessFailedCountAsync(user);
            }

            return Results.Ok(Issue(user, tokens));
        })
        .RequireRateLimiting(RateLimitSetup.AuthPolicy);

        // WHOAMI
        //
        // Cheap, and the only way a client holding a token can ask whether it is
        // still good without attempting a write.
        group.MapGet("/me", (ICurrentUser currentUser, HttpContext http) =>
            currentUser.Id is null
                ? Results.Unauthorized()
                : Results.Ok(new
                {
                    Id = currentUser.Id,
                    Email = http.User.Identity?.Name ?? string.Empty,
                }));
    }

    /// <summary>
    /// The one answer every failed sign-in gets, so that nothing about it — not
    /// the status, not the body — depends on which of the failures happened.
    /// </summary>
    private static IResult Unauthorized() =>
        Results.Problem(
            "Email or password is incorrect.",
            statusCode: StatusCodes.Status401Unauthorized);

    /// <summary>
    /// Burns one password verification when there is no password to verify.
    /// <para>
    /// Without this the endpoint says the same thing either way but takes a
    /// noticeably different time to say it: a real account costs a PBKDF2 hash,
    /// a made-up one costs an index lookup that misses. That gap is the identical
    /// message above undone by a stopwatch.
    /// </para>
    /// <para>
    /// Measured over a local socket, 24 samples each: no such account 34.8ms,
    /// locked account 34.8ms, wrong password on a live account 38.8ms. So the
    /// hash-shaped gap is closed — the remaining 4ms is not the lookup, which
    /// costs nothing measurable, but <c>AccessFailedAsync</c> writing the failure
    /// down. What still leaks is therefore "this address has an account that is
    /// not currently locked", and it leaks through the write that makes guessing
    /// pointless: five probes and the account joins the 34.8ms group for fifteen
    /// minutes. Combined with ten attempts a minute per IP, that is not a budget
    /// an enumeration attack can work with.
    /// </para>
    /// </summary>
    private static void PretendToCheckAPassword(IPasswordHasher<AppUser> hasher, string password)
    {
        // Hashed once and kept. Hashing per call would be the same work in the
        // wrong place: the decoy path would cost two hashes against the real
        // path's one, which is the timing gap again with the sign flipped. Two
        // threads racing to be first both produce a usable hash, so the last
        // writer winning costs nothing.
        //
        // Of random bytes rather than a fixed sentinel, so the verification
        // below always fails and the decoy never becomes a password somebody
        // could type.
        var hash = _decoyHash ??= hasher.HashPassword(
            Decoy,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        hasher.VerifyHashedPassword(Decoy, hash, password);
    }

    private static readonly AppUser Decoy = new();

    private static string? _decoyHash;

    private static AuthResponse Issue(AppUser user, JwtTokenService tokens)
    {
        var (token, expiresAt) = tokens.Create(user);

        return new AuthResponse
        {
            Token = token,
            ExpiresAtUtc = expiresAt,
            Email = user.Email ?? string.Empty,
        };
    }
}
