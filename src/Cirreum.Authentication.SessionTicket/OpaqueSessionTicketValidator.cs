namespace Cirreum.Authentication.SessionTicket;

using Cirreum.AuthenticationProvider.SessionTicket;
using Cirreum.AuthenticationProvider;
/// <summary>
/// Opaque-variant <see cref="ISessionTicketValidator"/>. Atomically consumes the inbound
/// ticket value from the <see cref="ISessionStore"/> (single-use), then re-checks expiry
/// before accepting it; returns the ticket on success, <see langword="null"/> otherwise.
/// Apps wanting reusable tickets plug in a different validator (one that uses
/// <see cref="ISessionStore.RetrieveAsync"/> instead of consuming).
/// </summary>
/// <remarks>
/// Single-use is the v1 default for opaque tickets; reusing the
/// same opaque token across multiple handshake attempts is a smell that usually
/// indicates a missed mint step in the negotiate / webhook flow. Consumption is a single
/// atomic store operation so concurrent handshakes presenting the same ticket cannot both
/// succeed.
/// </remarks>
public sealed class OpaqueSessionTicketValidator(ISessionStore store) : ISessionTicketValidator {

	/// <inheritdoc/>
	public async ValueTask<SessionTicket?> ValidateAsync(string ticketValue, CancellationToken cancellationToken) {

		if (string.IsNullOrWhiteSpace(ticketValue)) {
			return null;
		}

		// Single-use: atomically retrieve-and-remove. A non-atomic retrieve-then-remove
		// would let two concurrent handshakes both observe a hit before either removed it.
		var ticket = await store.ConsumeAsync(ticketValue, cancellationToken).ConfigureAwait(false);
		if (ticket is null) {
			return null;
		}

		// Defense in depth: never trust the store to have enforced expiry. Distributed
		// stores backed by best-effort TTL (e.g. Cosmos DB) can return a document whose
		// TTL has lapsed but whose background purge has not yet run.
		if (ticket.ExpiresAt <= DateTimeOffset.UtcNow) {
			return null;
		}

		return ticket;
	}

}
