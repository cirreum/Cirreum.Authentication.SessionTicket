namespace Cirreum.Authentication.SessionTicket;

using Cirreum.AuthenticationProvider.SessionTicket;
using Cirreum.AuthenticationProvider;
using System.Security.Cryptography;

/// <summary>
/// Opaque-variant <see cref="ISessionTicketIssuer"/>. Generates a cryptographically
/// random ticket value, optionally prepends the supplied <c>bearerPrefix</c>, builds a
/// <see cref="SessionTicket"/> with the supplied subject / lifetime / annotations,
/// persists it via <see cref="ISessionStore"/>, and returns the ticket to the caller
/// for downstream delivery to the partner / client.
/// </summary>
/// <remarks>
/// <para>
/// The opaque variant relies on the
/// store for validation — validators look up by ticket value. JWT-variant tickets
/// (deferred to 1.1.0+) embed the same data in a signed payload and validate
/// self-contained.
/// </para>
/// <para>
/// Auto-prepending the supplied <c>bearerPrefix</c> makes the framework's
/// Bearer-transport disambiguation work automatically — apps
/// that configure the prefix get prefix-based scheme selection without having to
/// re-prepend in their own code paths. The prefix is captured at construction by
/// the <c>AddSessionTicket(bearerPrefix)</c> verb.
/// </para>
/// </remarks>
public sealed class OpaqueSessionTicketIssuer(
	ISessionStore store,
	string? bearerPrefix = null
) : ISessionTicketIssuer {

	private const int TicketValueByteLength = 32;

	private readonly string? _bearerPrefix = bearerPrefix;

	/// <inheritdoc/>
	public async ValueTask<SessionTicket> IssueAsync(SessionTicketIssueRequest request, CancellationToken cancellationToken) {

		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Subject);

		if (request.Lifetime <= TimeSpan.Zero) {
			throw new ArgumentOutOfRangeException(
				nameof(request),
				$"{nameof(SessionTicketIssueRequest.Lifetime)} must be positive.");
		}

		var ticketValue = GenerateTicketValue();
		if (!string.IsNullOrEmpty(_bearerPrefix)) {
			ticketValue = _bearerPrefix + ticketValue;
		}

		var ticket = new SessionTicket {
			TicketValue = ticketValue,
			Subject = request.Subject,
			ExpiresAt = DateTimeOffset.UtcNow + request.Lifetime,
			Channel = request.Channel,
			Reference = request.Reference,
			Claims = request.Claims
		};

		await store.StoreAsync(ticket, cancellationToken).ConfigureAwait(false);

		return ticket;
	}

	private static string GenerateTicketValue() {
		Span<byte> bytes = stackalloc byte[TicketValueByteLength];
		RandomNumberGenerator.Fill(bytes);
		return Convert.ToHexStringLower(bytes);
	}

}
