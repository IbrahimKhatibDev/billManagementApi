using Microsoft.AspNetCore.Identity;

namespace BillsMinimalApi.Models;

/// <summary>
/// The account a <see cref="Bill"/> belongs to.
/// <para>
/// It adds nothing to <see cref="IdentityUser"/> yet, and exists anyway: the
/// alternative is <c>IdentityDbContext&lt;IdentityUser&gt;</c>, and every column
/// added to the user later — a display name, a plan — would then be a change of
/// the context's base type and a migration that renames nothing but rewrites the
/// model snapshot. Naming it once here makes that a one-line change instead.
/// </para>
/// </summary>
public class AppUser : IdentityUser
{
}
