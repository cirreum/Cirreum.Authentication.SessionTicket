namespace Cirreum.Authentication.SessionTicket;

using Cirreum.AuthenticationProvider.SessionTicket;
using Cirreum.AuthenticationProvider;
/// <summary>
/// Opaque-variant <see cref="ISessionTicketValidator"/>. Looks up the inbound
/// ticket value in the <see cref="ISessionStore"/>; returns the ticket on hit,
/// <see langword="null"/> otherwise. Single-use semantics are enforced by removing
/// the ticket from the store on successful retrieval — apps wanting reusable
/// tickets should plug in a different validator.
/// </summary>
/// <remarks>
/// Single-use is the v1 default for opaque tickets; reusing the
/// same opaque token across multiple handshake attempts is a smell that usually
/// indicates a missed mint step in the negotiate / webhook flow.
/// </remarks>
public sealed class OpaqueSessionTicketValidator(ISessionStore store) : ISessionTicketValidator {

	/// <inheritdoc/>
	public async ValueTask<SessionTicket?> ValidateAsync(string ticketValue, CancellationToken cancellationToken) {

		if (string.IsNullOrWhiteSpace(ticketValue)) {
			return null;
		}

		var ticket = await store.RetrieveAsync(ticketValue, cancellationToken).ConfigureAwait(false);
		if (ticket is null) {
			return null;
		}

		// Single-use: consume the ticket on first successful validation.
		await store.RemoveAsync(ticketValue, cancellationToken).ConfigureAwait(false);

		return ticket;
	}

}
