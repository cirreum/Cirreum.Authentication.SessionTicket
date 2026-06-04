namespace Cirreum.Authentication.SessionTicket.Tests;

using Cirreum.Authentication.SessionTicket;
using Cirreum.AuthenticationProvider.SessionTicket;

public sealed class OpaqueSessionTicketIssuerTests {

	private static SessionTicketIssueRequest Request(string subject = "alice") => new() {
		Subject = subject,
		Lifetime = TimeSpan.FromMinutes(2)
	};

	[Fact]
	public async Task IssueAsync_with_prefix_prepends_it_to_the_ticket_value() {
		using var store = new InMemorySessionStore();
		var issuer = new OpaqueSessionTicketIssuer(store, bearerPrefix: "st_prod_");

		var ticket = await issuer.IssueAsync(Request(), CancellationToken.None);

		ticket.TicketValue.Should().StartWith("st_prod_");
		// 32 bytes -> 64 hex chars, plus the prefix.
		ticket.TicketValue.Should().HaveLength("st_prod_".Length + 64);
	}

	[Fact]
	public async Task IssueAsync_persists_under_the_full_prefixed_value_so_the_validator_finds_it() {
		// This is the H-1 regression guard: the issuer stores under the prefixed value and
		// the validator looks it up by that same value. If the handler ever strips the prefix
		// again, the value handed to the validator stops matching the stored key.
		using var store = new InMemorySessionStore();
		var issuer = new OpaqueSessionTicketIssuer(store, bearerPrefix: "st_prod_");
		var validator = new OpaqueSessionTicketValidator(store);

		var issued = await issuer.IssueAsync(Request("alice"), CancellationToken.None);
		var validated = await validator.ValidateAsync(issued.TicketValue, CancellationToken.None);

		validated.Should().NotBeNull();
		validated!.Subject.Should().Be("alice");
	}

	[Fact]
	public async Task IssueAsync_without_prefix_round_trips() {
		using var store = new InMemorySessionStore();
		var issuer = new OpaqueSessionTicketIssuer(store, bearerPrefix: null);
		var validator = new OpaqueSessionTicketValidator(store);

		var issued = await issuer.IssueAsync(Request("bob"), CancellationToken.None);

		issued.TicketValue.Should().HaveLength(64);
		(await validator.ValidateAsync(issued.TicketValue, CancellationToken.None))!.Subject.Should().Be("bob");
	}

	[Fact]
	public async Task IssueAsync_produces_distinct_unguessable_values() {
		using var store = new InMemorySessionStore();
		var issuer = new OpaqueSessionTicketIssuer(store, bearerPrefix: null);

		var values = new HashSet<string>(StringComparer.Ordinal);
		for (var i = 0; i < 1_000; i++) {
			values.Add((await issuer.IssueAsync(Request(), CancellationToken.None)).TicketValue);
		}

		values.Should().HaveCount(1_000);
	}

	[Fact]
	public async Task IssueAsync_rejects_nonpositive_lifetime() {
		using var store = new InMemorySessionStore();
		var issuer = new OpaqueSessionTicketIssuer(store, bearerPrefix: null);

		var act = async () => await issuer.IssueAsync(
			new SessionTicketIssueRequest { Subject = "alice", Lifetime = TimeSpan.Zero },
			CancellationToken.None);

		await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
	}

	[Fact]
	public async Task IssueAsync_rejects_blank_subject() {
		using var store = new InMemorySessionStore();
		var issuer = new OpaqueSessionTicketIssuer(store, bearerPrefix: null);

		var act = async () => await issuer.IssueAsync(
			new SessionTicketIssueRequest { Subject = "   ", Lifetime = TimeSpan.FromMinutes(1) },
			CancellationToken.None);

		await act.Should().ThrowAsync<ArgumentException>();
	}

}
