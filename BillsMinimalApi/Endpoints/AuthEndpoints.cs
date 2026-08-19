using BillsMinimalApi.Auth;
using BillsMinimalApi.Dtos;
using BillsMinimalApi.Models;
using Microsoft.AspNetCore.Identity;

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
        });

        // LOGIN
        group.MapPost("/login", async (
            LoginRequest request,
            UserManager<AppUser> users,
            JwtTokenService tokens) =>
        {
            var user = await users.FindByEmailAsync(request.Email);

            // One branch for "no such account" and "wrong password" on purpose.
            // Answering them separately turns this endpoint into a way to ask
            // whether a given person has an account here.
            //
            // CheckPasswordAsync rather than SignInManager: there are no cookies
            // in this API and no lockout state to maintain, and SignInManager
            // drags in an authentication scheme that would exist only to be
            // unused.
            if (user is null || !await users.CheckPasswordAsync(user, request.Password))
            {
                return Results.Problem(
                    "Email or password is incorrect.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(Issue(user, tokens));
        });

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
