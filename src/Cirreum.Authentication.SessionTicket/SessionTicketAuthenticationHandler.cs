namespace Cirreum.Authentication.SessionTicket;

using Cirreum.AuthenticationProvider.SessionTicket;
using Cirreum.AuthenticationProvider;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Text.Encodings.Web;

/// <summary>
/// Authentication handler for the SessionTicket scheme. Reads the opaque ticket
/// value from <c>Authorization: Bearer</c>, strips the configured prefix (if any),
/// validates via <see cref="ISessionTicketValidator"/>, and emits the
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

		if (!string.IsNullOrEmpty(this.Options.BearerPrefix)
			&& ticketValue.StartsWith(this.Options.BearerPrefix, StringComparison.Ordinal)) {
			ticketValue = ticketValue[this.Options.BearerPrefix.Length..];
		}

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
		this.Response.StatusCode = 401;
		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	protected override Task HandleForbiddenAsync(AuthenticationProperties properties) {
		this.Response.StatusCode = 403;
		return Task.CompletedTask;
	}

}
