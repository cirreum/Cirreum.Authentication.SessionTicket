namespace Cirreum.Authentication.SessionTicket;

using Cirreum.AuthenticationProvider.SessionTicket;
using System.Security.Claims;

/// <summary>
/// Default <see cref="ISessionTicketPrincipalBinder"/> — builds a
/// <see cref="ClaimsPrincipal"/> from a validated <see cref="SessionTicket"/>, seeding
/// <c>sub</c> from the subject plus pass-through of any additional claims carried on
/// the ticket.
/// </summary>
/// <remarks>
/// <para>
/// The binder asserts only what the ticket knows: the subject identifier and the
/// credential marker. A session ticket is a continuation — it does not know its
/// subject's kind, let alone their display name — so no <c>name</c> claim is seeded.
/// An issuer that knows one passes <c>name</c> in
/// <see cref="SessionTicketIssueRequest.Claims"/>; the identity's name claim type is
/// <c>name</c>, so <c>Identity.Name</c> resolves when it does.
/// </para>
/// <para>
/// Pass-through claims that collide with the framework-owned identity claim types
/// (<c>sub</c>, <see cref="ClaimTypes.NameIdentifier"/>, and <c>client_type</c>) are
/// dropped so a ticket's <see cref="SessionTicket.Claims"/> bag cannot shadow or spoof
/// the bound subject's identifier. All other claim types — including roles — flow
/// through verbatim.
/// </para>
/// <para>
/// <strong>Trust boundary:</strong> pass-through claims become authorization-relevant
/// principal claims (e.g. roles drive <c>[Authorize(Roles = …)]</c>). The issuer is
/// responsible for ensuring <see cref="SessionTicketIssueRequest.Claims"/> is built from
/// trusted, already-authenticated context — never from unvalidated client input.
/// </para>
/// <para>
/// Apps with app-specific claim shapes (custom tenant identifiers,
/// non-standard role claims) register their own
/// <see cref="ISessionTicketPrincipalBinder"/> in DI and that registration wins.
/// </para>
/// </remarks>
public sealed class DefaultSessionTicketPrincipalBinder : ISessionTicketPrincipalBinder {

	private const string ClientTypeClaim = "client_type";
	private const string SubjectClaim = "sub";
	private const string NameClaim = "name";
	private const string RoleClaim = "roles";

	/// <summary>
	/// Claim types the binder owns and seeds itself — plus the legacy identifier
	/// equivalent — so pass-through claims of these types are dropped and a ticket
	/// cannot redefine the bound subject's identifier.
	/// </summary>
	private static readonly HashSet<string> ReservedClaimTypes = new(StringComparer.Ordinal) {
		SubjectClaim,
		ClientTypeClaim,
		ClaimTypes.NameIdentifier
	};

	/// <inheritdoc/>
	public ClaimsPrincipal BuildPrincipal(SessionTicket ticket) {

		ArgumentNullException.ThrowIfNull(ticket);

		var claims = new List<Claim> {
			new(SubjectClaim, ticket.Subject),
			new(ClientTypeClaim, "session_ticket")
		};

		if (ticket.Claims is not null) {
			foreach (var (claimType, claimValue) in ticket.Claims) {
				if (ReservedClaimTypes.Contains(claimType)) {
					// Framework-owned: a ticket cannot shadow the bound subject's identity.
					continue;
				}
				claims.Add(new Claim(claimType, claimValue));
			}
		}

		var identity = new ClaimsIdentity(
			claims,
			SessionTicketAuthenticationDefaults.AuthenticationScheme,
			nameType: NameClaim,
			roleType: RoleClaim);
		return new ClaimsPrincipal(identity);
	}

}
