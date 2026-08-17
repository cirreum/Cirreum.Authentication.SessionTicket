namespace Cirreum.Authentication.SessionTicket;

using Cirreum.AuthenticationProvider.SessionTicket;
using Cirreum.AuthenticationProvider;
using System.Security.Claims;

/// <summary>
/// Default <see cref="ISessionTicketPrincipalBinder"/> — builds a
/// <see cref="ClaimsPrincipal"/> from a validated <see cref="SessionTicket"/> using
/// the conventional <c>sub</c> / <c>name</c> mapping plus pass-through of any
/// additional claims carried on the ticket.
/// </summary>
/// <remarks>
/// <para>
/// Pass-through claims that collide with the framework-owned identity claim types
/// (<c>sub</c>, <c>name</c>, <c>client_type</c>, and their legacy equivalents
/// <see cref="ClaimTypes.NameIdentifier"/> / <see cref="ClaimTypes.Name"/>) are dropped so
/// a ticket's <see cref="SessionTicket.Claims"/> bag cannot shadow or spoof the bound
/// subject's identity. All other claim types — including roles — flow through verbatim.
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
	/// Claim types the binder owns and seeds itself — plus their legacy equivalents — so
	/// pass-through claims of these types are dropped and a ticket cannot redefine the
	/// bound identity.
	/// </summary>
	private static readonly HashSet<string> ReservedClaimTypes = new(StringComparer.Ordinal) {
		SubjectClaim,
		NameClaim,
		ClientTypeClaim,
		ClaimTypes.NameIdentifier,
		ClaimTypes.Name
	};

	/// <inheritdoc/>
	public ClaimsPrincipal BuildPrincipal(SessionTicket ticket) {

		ArgumentNullException.ThrowIfNull(ticket);

		var claims = new List<Claim> {
			new(SubjectClaim, ticket.Subject),
			new(NameClaim, ticket.Subject),
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
