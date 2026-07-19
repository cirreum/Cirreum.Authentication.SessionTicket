namespace Cirreum.Authentication.SessionTicket.Tests;

using Cirreum.AuthenticationProvider;
using Cirreum.AuthenticationProvider.SessionTicket;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Composition-path proofs for <c>AddSessionTicket()</c>: the full registration graph must compose on a
/// bare host and yield a resolvable container, and app-registered contract implementations must win over
/// the <c>TryAddSingleton</c> defaults (the documented extension seam). Guards the untested-composition-verb
/// escape vector behind Cirreum.Authentication.ApiKey issue #1, where the provider's composition verb threw
/// unconditionally through five published versions because no test ever invoked it.
/// </summary>
public sealed class AddSessionTicketCompositionTests {

	private static IAuthenticationBuilder CreateBuilder(IServiceCollection services) {
		var builder = Substitute.For<IAuthenticationBuilder>();
		builder.Services.Returns(services);
		builder.AuthBuilder.Returns(new AuthenticationBuilder(services));
		builder.Configuration.Returns(new ConfigurationBuilder().Build());
		return builder;
	}

	[Fact]
	public void AddSessionTicket_composes_without_throwing_and_resolves_the_default_graph() {
		var services = new ServiceCollection();
		var builder = CreateBuilder(services);

		builder.AddSessionTicket();

		using var provider = services.BuildServiceProvider(new ServiceProviderOptions {
			ValidateScopes = true,
		});
		provider.GetRequiredService<ISessionStore>().Should().BeOfType<InMemorySessionStore>();
		provider.GetRequiredService<ISessionTicketIssuer>().Should().BeOfType<OpaqueSessionTicketIssuer>();
		provider.GetRequiredService<ISessionTicketValidator>().Should().BeOfType<OpaqueSessionTicketValidator>();
		provider.GetRequiredService<ISessionTicketPrincipalBinder>().Should().BeOfType<DefaultSessionTicketPrincipalBinder>();

		// One selector instance serves both selector contracts.
		var schemeSelector = provider.GetRequiredService<ISchemeSelector>();
		var bearerSelector = provider.GetRequiredService<IBearerSchemeSelector>();
		schemeSelector.Should().BeOfType<SessionTicketAuthenticationSchemeSelector>();
		bearerSelector.Should().BeSameAs(schemeSelector);
	}

	[Fact]
	public void AddSessionTicket_with_a_prefix_still_resolves_the_issuer() {
		var services = new ServiceCollection();
		var builder = CreateBuilder(services);

		builder.AddSessionTicket("st_test_");

		using var provider = services.BuildServiceProvider();
		provider.GetRequiredService<ISessionTicketIssuer>().Should().BeOfType<OpaqueSessionTicketIssuer>();
	}

	[Fact]
	public void AddSessionTicket_keeps_an_app_registered_store_over_the_default() {
		var services = new ServiceCollection();
		var appStore = Substitute.For<ISessionStore>();
		services.AddSingleton(appStore);
		var builder = CreateBuilder(services);

		builder.AddSessionTicket();

		using var provider = services.BuildServiceProvider();
		provider.GetRequiredService<ISessionStore>().Should().BeSameAs(appStore);
	}

	[Fact]
	public void AddSessionTicket_called_twice_throws_the_composition_guard() {
		var services = new ServiceCollection();
		var builder = CreateBuilder(services);
		builder.AddSessionTicket();

		var act = () => builder.AddSessionTicket();

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*already been called*");
	}
}
