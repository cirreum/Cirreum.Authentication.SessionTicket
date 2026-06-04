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
/// <para>
/// Tickets past <see cref="SessionTicket.ExpiresAt"/> are evicted three ways: lazily on
/// <see cref="RetrieveAsync"/>, on <see cref="ConsumeAsync"/>, and proactively by a
/// background sweep so that minted-but-never-redeemed tickets cannot accumulate
/// unbounded (a memory-exhaustion vector under mint pressure). The sweep period is a
/// fixed minute — fine for the dev / single-head niche this store targets.
/// </para>
/// <para>
/// Registered as a singleton; the host disposes it on shutdown, which stops the sweep.
/// </para>
/// </remarks>
public sealed class InMemorySessionStore : ISessionStore, IDisposable {

	private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

	private readonly ConcurrentDictionary<string, SessionTicket> _byValue = new(StringComparer.Ordinal);
	private readonly Timer _sweepTimer;
	private int _sweeping;

	/// <summary>Creates the store and starts the background expiry sweep.</summary>
	public InMemorySessionStore() {
		_sweepTimer = new Timer(static state => ((InMemorySessionStore)state!).SweepExpired(), this, SweepInterval, SweepInterval);
	}

	/// <inheritdoc/>
	public ValueTask StoreAsync(SessionTicket ticket, CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(ticket);
		_byValue[ticket.TicketValue] = ticket;
		return ValueTask.CompletedTask;
	}

	/// <inheritdoc/>
	public ValueTask<SessionTicket?> ConsumeAsync(string ticketValue, CancellationToken cancellationToken) {
		// Atomic take: TryRemove returns the value AND removes it in one operation, so two
		// concurrent consumers cannot both observe a hit. This is the single-use guarantee.
		if (string.IsNullOrEmpty(ticketValue) || !_byValue.TryRemove(ticketValue, out var ticket)) {
			return ValueTask.FromResult<SessionTicket?>(null);
		}

		// Already removed either way; if it was past expiry, report a miss.
		return ValueTask.FromResult<SessionTicket?>(ticket.ExpiresAt <= DateTimeOffset.UtcNow ? null : ticket);
	}

	/// <inheritdoc/>
	public ValueTask<SessionTicket?> RetrieveAsync(string ticketValue, CancellationToken cancellationToken) {
		if (string.IsNullOrEmpty(ticketValue) || !_byValue.TryGetValue(ticketValue, out var ticket)) {
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
		if (!string.IsNullOrEmpty(ticketValue)) {
			_byValue.TryRemove(ticketValue, out _);
		}
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

	/// <summary>
	/// Removes every expired ticket. Invoked by the background sweep; re-entrancy-guarded
	/// so a slow sweep never overlaps itself.
	/// </summary>
	private void SweepExpired() {
		if (Interlocked.CompareExchange(ref _sweeping, 1, 0) != 0) {
			return;
		}

		try {
			var now = DateTimeOffset.UtcNow;
			foreach (var kvp in _byValue) {
				if (kvp.Value.ExpiresAt <= now) {
					_byValue.TryRemove(kvp.Key, out _);
				}
			}
		} finally {
			Interlocked.Exchange(ref _sweeping, 0);
		}
	}

	/// <inheritdoc/>
	public void Dispose() => _sweepTimer.Dispose();

}
