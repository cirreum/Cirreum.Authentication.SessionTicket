namespace Cirreum.Authentication.SessionTicket.Tests;

using Cirreum.Authentication.SessionTicket;
using Cirreum.AuthenticationProvider.SessionTicket;

public sealed class OpaqueSessionTicketValidatorTests {

	private static SessionTicket Ticket(string value, string subject = "alice", TimeSpan? ttl = null) => new() {
		TicketValue = value,
		Subject = subject,
		ExpiresAt = DateTimeOffset.UtcNow + (ttl ?? TimeSpan.FromMinutes(2))
	};

	[Fact]
	public async Task ValidateAsync_consumes_the_ticket_so_a_second_use_fails() {
		using var store = new InMemorySessionStore();
		await store.StoreAsync(Ticket("tok"), CancellationToken.None);
		var validator = new OpaqueSessionTicketValidator(store);

		(await validator.ValidateAsync("tok", CancellationToken.None)).Should().NotBeNull();
		(await validator.ValidateAsync("tok", CancellationToken.None)).Should().BeNull();
	}

	[Fact]
	public async Task ValidateAsync_returns_null_for_unknown_value() {
		using var store = new InMemorySessionStore();
		var validator = new OpaqueSessionTicketValidator(store);

		(await validator.ValidateAsync("nope", CancellationToken.None)).Should().BeNull();
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task ValidateAsync_returns_null_for_blank_value(string value) {
		using var store = new InMemorySessionStore();
		var validator = new OpaqueSessionTicketValidator(store);

		(await validator.ValidateAsync(value, CancellationToken.None)).Should().BeNull();
	}

	[Fact]
	public async Task ValidateAsync_under_concurrency_lets_exactly_one_caller_win() {
		// H-2: single-use must hold under a concurrent race. A non-atomic
		// retrieve-then-remove would let multiple callers observe the same hit.
		const int racers = 64;
		using var store = new InMemorySessionStore();
		await store.StoreAsync(Ticket("race"), CancellationToken.None);
		var validator = new OpaqueSessionTicketValidator(store);

		var start = new TaskCompletionSource();
		var tasks = Enumerable.Range(0, racers).Select(async _ => {
			await start.Task;
			return await validator.ValidateAsync("race", CancellationToken.None);
		}).ToArray();

		start.SetResult();
		var results = await Task.WhenAll(tasks);

		results.Count(r => r is not null).Should().Be(1);
	}

	[Fact]
	public async Task ValidateAsync_rejects_an_expired_ticket_from_the_store() {
		using var store = new InMemorySessionStore();
		await store.StoreAsync(Ticket("old", ttl: TimeSpan.FromMinutes(-1)), CancellationToken.None);
		var validator = new OpaqueSessionTicketValidator(store);

		(await validator.ValidateAsync("old", CancellationToken.None)).Should().BeNull();
	}

	[Fact]
	public async Task ValidateAsync_rechecks_expiry_even_if_the_store_returns_a_stale_ticket() {
		// M-1 defense in depth: a store backed by best-effort TTL (e.g. Cosmos) can hand back
		// a document whose TTL lapsed but whose purge has not run. The validator must still reject.
		var staleStore = Substitute.For<ISessionStore>();
		staleStore.ConsumeAsync("lapsed", Arg.Any<CancellationToken>())
			.Returns(new ValueTask<SessionTicket?>(Ticket("lapsed", ttl: TimeSpan.FromSeconds(-5))));
		var validator = new OpaqueSessionTicketValidator(staleStore);

		(await validator.ValidateAsync("lapsed", CancellationToken.None)).Should().BeNull();
	}

}
