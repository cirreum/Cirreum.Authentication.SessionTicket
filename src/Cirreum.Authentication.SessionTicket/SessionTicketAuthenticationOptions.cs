namespace Cirreum.Authentication.SessionTicket;

using Cirreum.AuthenticationProvider.SessionTicket;
using Microsoft.AspNetCore.Authentication;

/// <summary>
/// Options for the SessionTicket authentication handler. The v1.0 scheme handler is
/// bound to a single credential source —
/// <c>Authorization: Bearer</c> — at registration. Additional transports (cookie,
/// subprotocol, query parameter) will ship as separate ASP.NET schemes when
/// implemented in 1.x, each with its own scheme name + handler binding.
/// </summary>
public sealed class SessionTicketAuthenticationOptions : AuthenticationSchemeOptions {

	/// <summary>
	/// Optional Bearer-token prefix shared with <c>OpaqueSessionTicketIssuer</c>.
	/// The Bearer selector has already validated the prefix at probe time; the
	/// handler strips it from the inbound token before passing the remainder to
	/// the validator. <see langword="null"/> when no prefix is configured (the
	/// prefix-less fallback uses JWT-shape disambiguation in the selector).
	/// </summary>
	public string? BearerPrefix { get; set; }

}
