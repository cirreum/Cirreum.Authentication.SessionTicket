namespace Cirreum.Authentication.SessionTicket;

using Cirreum.AuthenticationProvider.SessionTicket;
using Cirreum.AuthenticationProvider;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Text.Encodings.Web;

/// <summary>
/// Authentication handler for the SessionTicket scheme. Reads the opaque ticket
/// value from <c>Authorization: Bearer</c>, validates the whole value (prefix
/// included) via <see cref="ISessionTicketValidator"/>, and emits the
/// <see cref="System.Security.Claims.ClaimsPrincipal"/> built by
/// <see cref="ISessionTicketPrincipalBinder"/>.
/// </summary>
/// <remarks>
/// <para>
/// The Bearer selector already disambiguated
/// the request as SessionTicket by the time the handler runs — either via the
/// configured prefix or by JWT-shape fallback. The handler does not re-check shape.
/// </para>
/// <para>
/// The configured Bearer prefix is part of the opaque ticket value, not a wrapper
/// to be peeled off: the issuer mints, persists, and returns the prefixed string as
/// a single secret (Stripe-style <c>st_prod_…</c>), so the handler validates the
/// value verbatim. Stripping here would look the ticket up under a key the issuer
/// never stored.
/// </para>
/// <para>
/// Apps mint tickets via <see cref="ISessionTicketIssuer"/> in their negotiate
/// endpoints or webhook handlers, return the ticket value to the partner / client,
/// and the partner / client presents the ticket as <c>Authorization: Bearer</c>
/// on follow-up calls.
/// </para>
/// </remarks>
public sealed class SessionTicketAuthenticationHandler(
	IOptionsMonitor<SessionTicketAuthenticationOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	ISessionTicketValidator validator,
	ISessionTicketPrincipalBinder principalBinder
) : AuthenticationHandler<SessionTicketAuthenticationOptions>(options, logger, encoder) {

	private const string BearerPrefixToken = "Bearer ";

	/// <inheritdoc/>
	protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {

		var authHeader = this.Request.Headers[HeaderNames.Authorization].ToString();
		if (string.IsNullOrEmpty(authHeader)
			|| !authHeader.StartsWith(BearerPrefixToken, StringComparison.OrdinalIgnoreCase)) {
			return AuthenticateResult.NoResult();
		}

		var ticketValue = authHeader[BearerPrefixToken.Length..].Trim();
		if (string.IsNullOrWhiteSpace(ticketValue)) {
			return AuthenticateResult.NoResult();
		}

		// The prefix is part of the opaque value; validate it verbatim. The issuer
		// stored the ticket under the full prefixed string, so the validator must
		// look it up by the same string — no stripping.
		var sessionTicket = await validator.ValidateAsync(ticketValue, this.Context.RequestAborted);
		if (sessionTicket is null) {
			if (this.Logger.IsEnabled(LogLevel.Warning)) {
				this.Logger.LogWarning("Session ticket validation failed");
			}
			return AuthenticateResult.Fail("Invalid or expired session ticket");
		}

		var principal = principalBinder.BuildPrincipal(sessionTicket);
		var authTicket = new AuthenticationTicket(principal, this.Scheme.Name);

		if (this.Logger.IsEnabled(LogLevel.Debug)) {
			this.Logger.LogDebug(
				"Session ticket authenticated for subject {Subject} via {Channel}",
				sessionTicket.Subject,
				sessionTicket.Channel ?? "(no channel)");
		}

		return AuthenticateResult.Success(authTicket);
	}

	/// <inheritdoc/>
	protected override Task HandleChallengeAsync(AuthenticationProperties properties) {
		this.Response.StatusCode = StatusCodes.Status401Unauthorized;
		// RFC 6750 §3: a 401 to a Bearer-credentialed scheme advertises the scheme so
		// clients know which credential to present.
		this.Response.Headers.Append(HeaderNames.WWWAuthenticate, "Bearer");
		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	protected override Task HandleForbiddenAsync(AuthenticationProperties properties) {
		this.Response.StatusCode = StatusCodes.Status403Forbidden;
		return Task.CompletedTask;
	}

}
