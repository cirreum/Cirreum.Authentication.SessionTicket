namespace Cirreum.Authentication.SessionTicket.Tests;

using Cirreum.Authentication.SessionTicket;
using Cirreum.AuthenticationProvider.SessionTicket;
using System.Security.Claims;

public sealed class DefaultSessionTicketPrincipalBinderTests {

	private static SessionTicket Ticket(string subject, IReadOnlyDictionary<string, string>? claims = null) => new() {
		TicketValue = "tok",
		Subject = subject,
		ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
		Claims = claims
	};

	private readonly DefaultSessionTicketPrincipalBinder _binder = new();

	[Fact]
	public void BuildPrincipal_maps_subject_to_name_and_nameidentifier() {
		var principal = _binder.BuildPrincipal(Ticket("alice"));

		principal.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("alice");
		principal.FindFirst(ClaimTypes.Name)!.Value.Should().Be("alice");
		principal.FindFirst("client_type")!.Value.Should().Be("session_ticket");
	}

	[Fact]
	public void BuildPrincipal_passes_through_non_reserved_claims_including_roles() {
		var principal = _binder.BuildPrincipal(Ticket("alice", new Dictionary<string, string> {
			["tenant"] = "acme",
			[ClaimTypes.Role] = "operator"
		}));

		principal.FindFirst("tenant")!.Value.Should().Be("acme");
		principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Should().Contain("operator");
	}

	[Fact]
	public void BuildPrincipal_does_not_let_passthrough_claims_shadow_the_bound_identity() {
		// M-2: a ticket's Claims bag must not be able to override the framework-owned identity.
		var principal = _binder.BuildPrincipal(Ticket("alice", new Dictionary<string, string> {
			[ClaimTypes.NameIdentifier] = "attacker",
			[ClaimTypes.Name] = "attacker",
			["client_type"] = "spoofed"
		}));

		principal.FindAll(ClaimTypes.NameIdentifier).Should().ContainSingle()
			.Which.Value.Should().Be("alice");
		principal.FindAll(ClaimTypes.Name).Should().ContainSingle()
			.Which.Value.Should().Be("alice");
		principal.FindAll("client_type").Should().ContainSingle()
			.Which.Value.Should().Be("session_ticket");
	}

	[Fact]
	public void BuildPrincipal_rejects_null_ticket() {
		var act = () => _binder.BuildPrincipal(null!);

		act.Should().Throw<ArgumentNullException>();
	}

}
