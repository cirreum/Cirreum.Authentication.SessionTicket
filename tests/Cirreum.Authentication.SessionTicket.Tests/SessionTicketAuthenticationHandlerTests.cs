namespace Cirreum.Authentication.SessionTicket.Tests;

using Cirreum.Authentication;
using Cirreum.Authentication.SessionTicket;
using Cirreum.AuthenticationProvider.SessionTicket;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

public sealed class SessionTicketAuthenticationHandlerTests {

	private static async Task<(SessionTicketAuthenticationHandler Handler, DefaultHttpContext Context)> CreateAsync(
		ISessionStore store,
		string? authorizationHeader) {

		var monitor = Substitute.For<IOptionsMonitor<SessionTicketAuthenticationOptions>>();
		monitor.Get(Arg.Any<string>()).Returns(new SessionTicketAuthenticationOptions());

		var handler = new SessionTicketAuthenticationHandler(
			monitor,
			NullLoggerFactory.Instance,
			UrlEncoder.Default,
			new OpaqueSessionTicketValidator(store),
			new DefaultSessionTicketPrincipalBinder());

		var context = new DefaultHttpContext();
		if (authorizationHeader is not null) {
			context.Request.Headers["Authorization"] = authorizationHeader;
		}

		var scheme = new AuthenticationScheme(
			SessionTicketSchemes.Bearer, displayName: null, typeof(SessionTicketAuthenticationHandler));
		await handler.InitializeAsync(scheme, context);
		return (handler, context);
	}

	[Fact]
	public async Task Authenticate_succeeds_for_a_prefixed_ticket_minted_by_the_issuer() {
		// H-1 end to end: issuer stores under the prefixed value; the handler validates the
		// Authorization: Bearer value verbatim (no stripping) so the lookup matches.
		using var store = new InMemorySessionStore();
		var issuer = new OpaqueSessionTicketIssuer(store, bearerPrefix: "st_prod_");
		var ticket = await issuer.IssueAsync(
			new SessionTicketIssueRequest { Subject = "alice", Lifetime = TimeSpan.FromMinutes(2) },
			CancellationToken.None);

		var (handler, _) = await CreateAsync(store, $"Bearer {ticket.TicketValue}");
		var result = await handler.AuthenticateAsync();

		result.Succeeded.Should().BeTrue();
		result.Principal!.Identity!.Name.Should().Be("alice");
		result.Principal.FindFirst("client_type")!.Value.Should().Be("session_ticket");
	}

	[Fact]
	public async Task Authenticate_flows_passthrough_claims_onto_the_principal() {
		using var store = new InMemorySessionStore();
		var issuer = new OpaqueSessionTicketIssuer(store, bearerPrefix: "st_prod_");
		var ticket = await issuer.IssueAsync(
			new SessionTicketIssueRequest {
				Subject = "alice",
				Lifetime = TimeSpan.FromMinutes(2),
				Claims = new Dictionary<string, string> { [ClaimTypes.Role] = "operator" }
			},
			CancellationToken.None);

		var (handler, _) = await CreateAsync(store, $"Bearer {ticket.TicketValue}");
		var result = await handler.AuthenticateAsync();

		result.Succeeded.Should().BeTrue();
		result.Principal!.FindAll(ClaimTypes.Role).Select(c => c.Value).Should().Contain("operator");
	}

	[Fact]
	public async Task Authenticate_returns_no_result_when_authorization_header_absent() {
		using var store = new InMemorySessionStore();
		var (handler, _) = await CreateAsync(store, authorizationHeader: null);

		(await handler.AuthenticateAsync()).None.Should().BeTrue();
	}

	[Fact]
	public async Task Authenticate_returns_no_result_for_non_bearer_header() {
		using var store = new InMemorySessionStore();
		var (handler, _) = await CreateAsync(store, "Basic dXNlcjpwYXNz");

		(await handler.AuthenticateAsync()).None.Should().BeTrue();
	}

	[Fact]
	public async Task Authenticate_fails_for_an_unknown_ticket() {
		using var store = new InMemorySessionStore();
		var (handler, _) = await CreateAsync(store, "Bearer st_prod_deadbeef");

		var result = await handler.AuthenticateAsync();

		result.Succeeded.Should().BeFalse();
		result.Failure.Should().NotBeNull();
	}

	[Fact]
	public async Task Authenticate_fails_on_the_second_use_of_a_single_use_ticket() {
		using var store = new InMemorySessionStore();
		var issuer = new OpaqueSessionTicketIssuer(store, bearerPrefix: "st_prod_");
		var ticket = await issuer.IssueAsync(
			new SessionTicketIssueRequest { Subject = "alice", Lifetime = TimeSpan.FromMinutes(2) },
			CancellationToken.None);
		var header = $"Bearer {ticket.TicketValue}";

		var (firstHandler, _) = await CreateAsync(store, header);
		(await firstHandler.AuthenticateAsync()).Succeeded.Should().BeTrue();

		var (secondHandler, _) = await CreateAsync(store, header);
		(await secondHandler.AuthenticateAsync()).Succeeded.Should().BeFalse();
	}

	[Fact]
	public async Task Challenge_writes_401_with_bearer_www_authenticate() {
		using var store = new InMemorySessionStore();
		var (handler, context) = await CreateAsync(store, authorizationHeader: null);

		await handler.ChallengeAsync(new AuthenticationProperties());

		context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
		context.Response.Headers.WWWAuthenticate.ToString().Should().Be("Bearer");
	}

}
