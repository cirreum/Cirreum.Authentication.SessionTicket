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
	public void BuildPrincipal_maps_subject_to_sub_and_fabricates_no_name() {
		var principal = _binder.BuildPrincipal(Ticket("alice"));

		principal.FindFirst("sub")!.Value.Should().Be("alice");
		principal.FindFirst("client_type")!.Value.Should().Be("session_ticket");

		// A continuation does not know its subject's kind, let alone their display
		// name — none is fabricated. Role checks read "roles".
		principal.FindFirst("name").Should().BeNull();
		principal.Identity!.Name.Should().BeNull();
		((ClaimsIdentity)principal.Identity).RoleClaimType.Should().Be("roles");
	}

	[Fact]
	public void BuildPrincipal_lets_an_issuer_supplied_name_drive_identity_name() {
		var principal = _binder.BuildPrincipal(Ticket("alice-id", new Dictionary<string, string> {
			["name"] = "Alice Example"
		}));

		principal.FindFirst("sub")!.Value.Should().Be("alice-id");
		principal.Identity!.Name.Should().Be("Alice Example");
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
			["sub"] = "attacker",
			[ClaimTypes.NameIdentifier] = "attacker",
			["client_type"] = "spoofed"
		}));

		principal.FindAll("sub").Should().ContainSingle()
			.Which.Value.Should().Be("alice");
		principal.FindAll(ClaimTypes.NameIdentifier).Should().BeEmpty();
		principal.FindAll("client_type").Should().ContainSingle()
			.Which.Value.Should().Be("session_ticket");
	}

	[Fact]
	public void BuildPrincipal_rejects_null_ticket() {
		var act = () => _binder.BuildPrincipal(null!);

		act.Should().Throw<ArgumentNullException>();
	}

}
