namespace Cirreum.Authentication.SessionTicket;
/// <summary>
/// Default constants for the SessionTicket authentication scheme.
/// </summary>
public static class SessionTicketAuthenticationDefaults {

	/// <summary>
	/// The default ASP.NET Core authentication scheme name. By the
	/// multi-scheme naming convention, the SessionTicket
	/// scheme is suffixed by its transport identity (<c>:Bearer</c>). A future
	/// transport would ship as a separate scheme (e.g. <c>SessionTicket:Subprotocol</c>).
	/// </summary>
	public const string AuthenticationScheme = "SessionTicket:Bearer";

	/// <summary>The default WebSocket subprotocol prefix carrying the ticket value
	/// (the full subprotocol becomes <c>"cirreum-st.{ticketValue}"</c>).</summary>
	public const string DefaultSubprotocolPrefix = "cirreum-st.";

}
