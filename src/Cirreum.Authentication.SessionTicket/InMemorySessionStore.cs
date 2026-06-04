namespace Cirreum.Authentication.SessionTicket;

using Cirreum.AuthenticationProvider.SessionTicket;
using Cirreum.AuthenticationProvider;
using System.Collections.Concurrent;

/// <summary>
/// Default in-memory <see cref="ISessionStore"/> implementation suitable for
/// development and single-head deployments. Multi-head production apps plug in a
/// distributed store (Redis, Cosmos DB, etc.) via their persistence track.
/// </summary>
/// <remarks>
/// Tickets past <see cref="SessionTicket.ExpiresAt"/> are evicted lazily on read.
/// </remarks>
public sealed class InMemorySessionStore : ISessionStore {

	private readonly ConcurrentDictionary<string, SessionTicket> _byValue = new(StringComparer.Ordinal);

	/// <inheritdoc/>
	public ValueTask StoreAsync(SessionTicket ticket, CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(ticket);
		_byValue[ticket.TicketValue] = ticket;
		return ValueTask.CompletedTask;
	}

	/// <inheritdoc/>
	public ValueTask<SessionTicket?> RetrieveAsync(string ticketValue, CancellationToken cancellationToken) {
		if (!_byValue.TryGetValue(ticketValue, out var ticket)) {
			return ValueTask.FromResult<SessionTicket?>(null);
		}

		if (ticket.ExpiresAt <= DateTimeOffset.UtcNow) {
			_byValue.TryRemove(ticketValue, out _);
			return ValueTask.FromResult<SessionTicket?>(null);
		}

		return ValueTask.FromResult<SessionTicket?>(ticket);
	}

	/// <inheritdoc/>
	public ValueTask RemoveAsync(string ticketValue, CancellationToken cancellationToken) {
		_byValue.TryRemove(ticketValue, out _);
		return ValueTask.CompletedTask;
	}

	/// <inheritdoc/>
	public ValueTask RemoveBySubjectAsync(string subject, CancellationToken cancellationToken) {
		foreach (var kvp in _byValue) {
			if (string.Equals(kvp.Value.Subject, subject, StringComparison.Ordinal)) {
				_byValue.TryRemove(kvp.Key, out _);
			}
		}
		return ValueTask.CompletedTask;
	}

}
