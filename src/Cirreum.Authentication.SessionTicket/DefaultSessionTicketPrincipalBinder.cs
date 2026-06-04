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
/// Apps with app-specific claim shapes (custom tenant identifiers,
/// non-standard role claims) register their own
/// <see cref="ISessionTicketPrincipalBinder"/> in DI and that registration wins.
/// </remarks>
public sealed class DefaultSessionTicketPrincipalBinder : ISessionTicketPrincipalBinder {

	/// <inheritdoc/>
	public ClaimsPrincipal BuildPrincipal(SessionTicket ticket) {

		ArgumentNullException.ThrowIfNull(ticket);

		var claims = new List<Claim> {
			new(ClaimTypes.NameIdentifier, ticket.Subject),
			new(ClaimTypes.Name, ticket.Subject),
			new("client_type", "session_ticket")
		};

		if (ticket.Claims is not null) {
			foreach (var (claimType, claimValue) in ticket.Claims) {
				claims.Add(new Claim(claimType, claimValue));
			}
		}

		var identity = new ClaimsIdentity(claims, SessionTicketAuthenticationDefaults.AuthenticationScheme);
		return new ClaimsPrincipal(identity);
	}

}
