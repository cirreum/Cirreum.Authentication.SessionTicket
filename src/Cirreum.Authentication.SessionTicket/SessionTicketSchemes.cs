namespace Cirreum.Authentication;

using Cirreum.Authentication.SessionTicket;

/// <summary>
/// The ASP.NET authentication scheme name(s) for the SessionTicket provider. App authors reference
/// these in policy definitions
/// (<c>.AddAuthenticationSchemes(SessionTicketSchemes.Bearer)</c>) and anywhere a scheme name is
/// required.
/// </summary>
/// <remarks>
/// SessionTicket ships only the Bearer transport in v1.0 (cookie, subprotocol,
/// and query-parameter transports deferred to 1.x), so the surface is a single <see cref="Bearer"/>
/// constant rather than a transport-keyed family.
/// </remarks>
public static class SessionTicketSchemes {

	/// <summary>The SessionTicket Bearer-transport scheme name — <c>SessionTicket:Bearer</c>.</summary>
	public const string Bearer = SessionTicketAuthenticationDefaults.AuthenticationScheme;

}
