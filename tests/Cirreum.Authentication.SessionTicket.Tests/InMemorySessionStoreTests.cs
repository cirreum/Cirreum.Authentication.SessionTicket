namespace Cirreum.Authentication.SessionTicket.Tests;

using Cirreum.Authentication.SessionTicket;
using Cirreum.AuthenticationProvider.SessionTicket;

public sealed class InMemorySessionStoreTests {

	private static SessionTicket Ticket(string value, string subject = "alice", TimeSpan? ttl = null) => new() {
		TicketValue = value,
		Subject = subject,
		ExpiresAt = DateTimeOffset.UtcNow + (ttl ?? TimeSpan.FromMinutes(2))
	};

	[Fact]
	public async Task ConsumeAsync_returns_then_removes_the_ticket() {
		using var store = new InMemorySessionStore();
		await store.StoreAsync(Ticket("tok"), CancellationToken.None);

		(await store.ConsumeAsync("tok", CancellationToken.None)).Should().NotBeNull();
		(await store.ConsumeAsync("tok", CancellationToken.None)).Should().BeNull();
		(await store.RetrieveAsync("tok", CancellationToken.None)).Should().BeNull();
	}

	[Fact]
	public async Task ConsumeAsync_under_concurrency_hands_the_ticket_to_exactly_one_caller() {
		const int racers = 64;
		using var store = new InMemorySessionStore();
		await store.StoreAsync(Ticket("race"), CancellationToken.None);

		var start = new TaskCompletionSource();
		var tasks = Enumerable.Range(0, racers).Select(async _ => {
			await start.Task;
			return await store.ConsumeAsync("race", CancellationToken.None);
		}).ToArray();

		start.SetResult();
		var results = await Task.WhenAll(tasks);

		results.Count(r => r is not null).Should().Be(1);
	}

	[Fact]
	public async Task ConsumeAsync_treats_an_expired_ticket_as_a_miss() {
		using var store = new InMemorySessionStore();
		await store.StoreAsync(Ticket("old", ttl: TimeSpan.FromMinutes(-1)), CancellationToken.None);

		(await store.ConsumeAsync("old", CancellationToken.None)).Should().BeNull();
	}

	[Fact]
	public async Task RetrieveAsync_evicts_an_expired_ticket_on_read() {
		using var store = new InMemorySessionStore();
		await store.StoreAsync(Ticket("old", ttl: TimeSpan.FromMinutes(-1)), CancellationToken.None);

		(await store.RetrieveAsync("old", CancellationToken.None)).Should().BeNull();
		// Re-storing a live ticket under the same value works (eviction freed the slot cleanly).
		await store.StoreAsync(Ticket("old"), CancellationToken.None);
		(await store.RetrieveAsync("old", CancellationToken.None)).Should().NotBeNull();
	}

	[Fact]
	public async Task RetrieveAsync_does_not_consume() {
		using var store = new InMemorySessionStore();
		await store.StoreAsync(Ticket("tok"), CancellationToken.None);

		(await store.RetrieveAsync("tok", CancellationToken.None)).Should().NotBeNull();
		(await store.RetrieveAsync("tok", CancellationToken.None)).Should().NotBeNull();
	}

	[Fact]
	public async Task RemoveBySubjectAsync_removes_every_ticket_for_that_subject_only() {
		using var store = new InMemorySessionStore();
		await store.StoreAsync(Ticket("a1", subject: "alice"), CancellationToken.None);
		await store.StoreAsync(Ticket("a2", subject: "alice"), CancellationToken.None);
		await store.StoreAsync(Ticket("b1", subject: "bob"), CancellationToken.None);

		await store.RemoveBySubjectAsync("alice", CancellationToken.None);

		(await store.RetrieveAsync("a1", CancellationToken.None)).Should().BeNull();
		(await store.RetrieveAsync("a2", CancellationToken.None)).Should().BeNull();
		(await store.RetrieveAsync("b1", CancellationToken.None)).Should().NotBeNull();
	}

}
