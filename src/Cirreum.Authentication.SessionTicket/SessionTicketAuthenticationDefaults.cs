namespace Cirreum.Authentication.SessionTicket;

using Cirreum.AuthenticationProvider.SessionTicket;
/// <summary>
/// Default constants for the SessionTicket authentication scheme.
/// </summary>
public static class SessionTicketAuthenticationDefaults {

	/// <summary>
	/// The default ASP.NET Core authentication scheme name. By the
	/// multi-scheme naming convention, the v1.0 SessionTicket
	/// scheme is suffixed by its transport identity (<c>:Bearer</c>). Additional
	/// transports (cookie, subprotocol, query parameter) will ship as separate
	/// schemes (e.g. <c>SessionTicket:Cookie</c>) in 1.x.
	/// </summary>
	public const string AuthenticationScheme = "SessionTicket:Bearer";

	/// <summary>The default cookie name carrying the opaque ticket value.</summary>
	public const string DefaultCookieName = "cirreum.sessionticket";

	/// <summary>The default query-string parameter name (alternate transport).</summary>
	public const string DefaultQueryParameterName = "ticket";

	/// <summary>The default WebSocket subprotocol prefix carrying the ticket value
	/// (the full subprotocol becomes <c>"cirreum-st.{ticketValue}"</c>).</summary>
	public const string DefaultSubprotocolPrefix = "cirreum-st.";

}
