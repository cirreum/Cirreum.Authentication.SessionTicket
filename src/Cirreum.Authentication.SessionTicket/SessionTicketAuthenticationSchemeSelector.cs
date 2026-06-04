namespace Cirreum.Authentication.SessionTicket;

using Cirreum.AuthenticationProvider.SessionTicket;
using Cirreum.AuthenticationProvider;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

/// <summary>
/// <see cref="IBearerSchemeSelector"/> implementation for the SessionTicket scheme.
/// Probes <c>Authorization: Bearer</c>; claims when the configured
/// <see cref="BearerPrefix"/> matches the inbound token, or — when no prefix is
/// configured — when the token is non-empty and not JWT-shaped (prefix-less fallback).
/// </summary>
/// <remarks>
/// <para>
/// Registered at
/// <see cref="SchemeSelectorPriority.Session"/> — runs after ApiKey + SignedRequest
/// and before External / Audience.
/// </para>
/// <para>
/// When the configured <see cref="BearerPrefix"/> is set, JWT-shape is irrelevant —
/// the prefix has already committed dispatch.
/// </para>
/// </remarks>
public sealed class SessionTicketAuthenticationSchemeSelector(
	string schemeName,
	string? bearerPrefix
) : IBearerSchemeSelector {

	private const string BearerPrefixToken = "Bearer ";

	/// <inheritdoc/>
	public int Priority => SchemeSelectorPriority.Session;

	/// <inheritdoc/>
	public string? BearerPrefix { get; } = bearerPrefix;

	/// <inheritdoc/>
	public (bool Matches, string? SchemeName) TrySelect(HttpContext context) {

		if (context is null) {
			return (false, null);
		}

		var authHeader = context.Request.Headers[HeaderNames.Authorization].ToString();
		if (string.IsNullOrEmpty(authHeader)
			|| !authHeader.StartsWith(BearerPrefixToken, StringComparison.OrdinalIgnoreCase)) {
			return (false, null);
		}

		var token = authHeader[BearerPrefixToken.Length..].Trim();
		if (string.IsNullOrEmpty(token)) {
			return (false, null);
		}

		if (!string.IsNullOrEmpty(this.BearerPrefix)) {
			return token.StartsWith(this.BearerPrefix, StringComparison.Ordinal)
				? (true, schemeName)
				: (false, null);
		}

		// Prefix-less fallback: claim opaque (non-JWT) Bearer values.
		return IsJwtShape(token) ? (false, null) : (true, schemeName);
	}

	private static bool IsJwtShape(string value) {
		var firstDot = value.IndexOf('.');
		if (firstDot <= 0 || firstDot == value.Length - 1) {
			return false;
		}
		var secondDot = value.IndexOf('.', firstDot + 1);
		return secondDot > firstDot && secondDot < value.Length - 1
			&& value.IndexOf('.', secondDot + 1) == -1;
	}

}
