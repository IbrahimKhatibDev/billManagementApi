using System.Net;
using System.Net.Http.Json;
using BillsMinimalApi.Data;
using BillsMinimalApi.Dtos;

namespace BillsMinimalApi.Tests;

/// <summary>
/// What <c>POST /auth/login</c> gives away, and what it stops giving away after
/// enough wrong guesses.
/// </summary>
public sealed class LoginHardeningTests : ApiTestBase
{
    private const string GoodPassword = "Test-Password-1";

    private const string BadPassword = "not-the-password";

    public LoginHardeningTests(PostgresApiFixture fixture) : base(fixture) { }

    [Fact]
    public async Task An_unknown_account_and_a_wrong_password_get_the_same_answer()
    {
        var email = await RegisterThrowawayAsync();

        var wrongPassword = await LoginAsync(email, BadPassword);
        var noSuchAccount = await LoginAsync($"never-registered-{Guid.NewGuid():N}@tests.local", BadPassword);

        // Byte for byte, not merely both-401. A difference in the body is the
        // same disclosure as a difference in the status: either one answers "does
        // this person have an account here?", which is a question this endpoint
        // should not answer to someone who cannot already sign in.
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.Status);
        Assert.Equal(noSuchAccount.Status, wrongPassword.Status);
        Assert.Equal(noSuchAccount.Body, wrongPassword.Body);
    }

    [Fact]
    public async Task A_locked_account_looks_exactly_like_a_wrong_password()
    {
        var locked = await RegisterThrowawayAsync();
        await FailSignInAsync(locked, times: 5);

        // The correct password now, which is the point: the account is locked,
        // and saying so would tell an attacker they had just found the password.
        var lockedOut = await LoginAsync(locked, GoodPassword);

        var other = await RegisterThrowawayAsync();
        var wrongPassword = await LoginAsync(other, BadPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, lockedOut.Status);
        Assert.Equal(wrongPassword.Body, lockedOut.Body);
    }

    [Fact]
    public async Task Five_wrong_passwords_stop_the_account_answering()
    {
        var email = await RegisterThrowawayAsync();

        // Four is still fine — the fifth failure is what trips it, so a person
        // who has fumbled four times can still get in.
        await FailSignInAsync(email, times: 4);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, GoodPassword)).Status);

        await FailSignInAsync(email, times: 5);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, GoodPassword)).Status);
    }

    [Fact]
    public async Task Signing_in_forgives_the_failures_before_it()
    {
        var email = await RegisterThrowawayAsync();

        // Nine wrong passwords in total, but never five in a row without a
        // success between them. Without the reset the fifth cumulative failure
        // would lock the account and the final sign-in would fail — which is the
        // difference between "five wrong guesses" and "five wrong guesses ever",
        // the second of which locks out anyone who has owned the account long
        // enough to mistype it occasionally.
        await FailSignInAsync(email, times: 4);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, GoodPassword)).Status);

        await FailSignInAsync(email, times: 4);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, GoodPassword)).Status);
    }

    [Fact]
    public async Task The_lockout_is_per_account_and_not_per_caller()
    {
        var locked = await RegisterThrowawayAsync();
        var bystander = await RegisterThrowawayAsync();

        await FailSignInAsync(locked, times: 5);

        // Same client, same connection — so if the lockout attached to the caller
        // rather than to the account, this would fail too. It has to be the
        // account: the caller is an IP address, and an attacker has more of those
        // than this app has accounts.
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(bystander, GoodPassword)).Status);
    }

    [Fact]
    public async Task The_demo_account_cannot_be_locked_out()
    {
        // The exception to every test above it, and the reason the seeder turns
        // the flag off: this password is published in the README, so the lockout
        // protects nothing and any visitor could use it to take the demo down
        // for fifteen minutes at a time.
        await FailSignInAsync(DbSeeder.DemoEmail, times: 5);

        Assert.Equal(
            HttpStatusCode.OK,
            (await LoginAsync(DbSeeder.DemoEmail, DbSeeder.DemoPassword)).Status);
    }

    /// <summary>
    /// A fresh account per test. These tests lock accounts out for fifteen
    /// minutes and <see cref="PostgresApiFixture.ResetAsync"/> deliberately
    /// leaves AspNetUsers alone, so borrowing the fixture's shared user would
    /// lock it for the rest of the run and fail every later test that signs in.
    /// </summary>
    private async Task<string> RegisterThrowawayAsync()
    {
        var email = $"lockout-{Guid.NewGuid():N}@tests.local";

        var response = await Fixture.AnonymousClient.PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Email = email,
            Password = GoodPassword,
        });

        response.EnsureSuccessStatusCode();

        return email;
    }

    private async Task FailSignInAsync(string email, int times)
    {
        for (var i = 0; i < times; i++)
        {
            var attempt = await LoginAsync(email, BadPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.Status);
        }
    }

    private async Task<(HttpStatusCode Status, string Body)> LoginAsync(string email, string password)
    {
        using var response = await Fixture.AnonymousClient.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Email = email,
            Password = password,
        });

        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
