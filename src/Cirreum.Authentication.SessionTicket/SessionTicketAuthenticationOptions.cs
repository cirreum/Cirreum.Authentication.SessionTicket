namespace Cirreum.Authentication.SessionTicket;

using Microsoft.AspNetCore.Authentication;

/// <summary>
/// Options for the SessionTicket authentication handler. The scheme handler is
/// bound to a single credential source —
/// <c>Authorization: Bearer</c> — at registration. A future transport (subprotocol)
/// would ship as a separate ASP.NET scheme with its own scheme name + handler binding.
/// </summary>
/// <remarks>
/// The Bearer prefix is intentionally <em>not</em> an option here. It is part of the
/// opaque ticket value the issuer mints and persists, and it is consumed only by the
/// <see cref="SessionTicketAuthenticationSchemeSelector"/> for dispatch — the handler
/// validates the value verbatim, so it has no prefix knob to configure.
/// </remarks>
public sealed class SessionTicketAuthenticationOptions : AuthenticationSchemeOptions {

}
