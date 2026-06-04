namespace Cirreum.Authentication;

using Cirreum.Authentication.SessionTicket;
using Cirreum.AuthenticationProvider;
using Cirreum.AuthenticationProvider.SessionTicket;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// The <c>AddSessionTicket(...)</c> composition verb contributed by the SessionTicket scheme package.
/// Available inside the <c>configure</c> callback of <c>AddAuthentication(...)</c> on the umbrella package <c>Cirreum.Runtime.Authentication</c>.
/// </summary>
/// <remarks>
/// <para>
/// SessionTicket is <b>code-composed</b> — there is no appsettings section or registrar (per the
/// Cirreum provider model, appsettings and a registrar are a matched pair, and SessionTicket has
/// no per-instance data — the only configurable knob, <c>bearerPrefix</c>, is provider-level
/// and supplied at the call site).
/// </para>
/// <para>
/// All four supporting services — <see cref="ISessionStore"/>, <see cref="ISessionTicketIssuer"/>,
/// <see cref="ISessionTicketValidator"/>, <see cref="ISessionTicketPrincipalBinder"/> — are
/// registered with <c>TryAddSingleton</c> defaults. Apps that need custom implementations (e.g. a
/// Redis-backed store, an app-specific principal binder) register their type in DI <b>before</b>
/// calling <c>AddSessionTicket</c> and the framework defaults skip naturally.
/// </para>
/// </remarks>
public static class SessionTicketAuthenticationBuilderExtensions {

	private sealed class SessionTicketComposedMarker { }

	/// <summary>
	/// Composes the SessionTicket authentication scheme. Registers the default opaque issuer /
	/// validator + in-memory store + default principal binder (all via <c>TryAddSingleton</c>),
	/// the <see cref="SessionTicketSchemes.Bearer"/> ASP.NET scheme + handler, and the Bearer
	/// scheme selector.
	/// </summary>
	/// <param name="builder">The Cirreum authentication builder.</param>
	/// <param name="bearerPrefix">Optional Bearer-token prefix. When set, the
	/// framework-recommended shape is <c>{scheme}_{env}_{raw}</c> (for example, <c>st_prod_</c>).
	/// The default <see cref="OpaqueSessionTicketIssuer"/> auto-prepends this prefix when minting
	/// tickets so the Bearer-transport disambiguation works automatically; the
	/// scheme selector strips it before the handler validates. When <see langword="null"/> the
	/// selector falls back to JWT-shape disambiguation (claims any non-JWT-shaped opaque Bearer
	/// token).</param>
	/// <returns>The builder for chaining.</returns>
	public static IAuthenticationBuilder AddSessionTicket(
		this IAuthenticationBuilder builder,
		string? bearerPrefix = null) {

		ArgumentNullException.ThrowIfNull(builder);

		var services = builder.Services;
		if (services.Any(d => d.ServiceType == typeof(SessionTicketComposedMarker))) {
			throw new InvalidOperationException(
				"AddSessionTicket() has already been called for this host. Call it once during composition.");
		}
		services.AddSingleton<SessionTicketComposedMarker>();

		// Default contract implementations. App-supplied implementations win via TryAddSingleton.
		services.TryAddSingleton<ISessionStore, InMemorySessionStore>();
		services.TryAddSingleton<ISessionTicketIssuer>(sp =>
			new OpaqueSessionTicketIssuer(sp.GetRequiredService<ISessionStore>(), bearerPrefix));
		services.TryAddSingleton<ISessionTicketValidator, OpaqueSessionTicketValidator>();
		services.TryAddSingleton<ISessionTicketPrincipalBinder, DefaultSessionTicketPrincipalBinder>();

		// Single scheme + handler + selector. The prefix drives the issuer (minting)
		// and the selector (dispatch); the handler validates the value verbatim, so
		// the scheme options carry no prefix.
		var schemeName = SessionTicketSchemes.Bearer;
		builder.AuthBuilder.AddScheme<SessionTicketAuthenticationOptions, SessionTicketAuthenticationHandler>(
			schemeName,
			static _ => { });

		services.AddSingleton(new SessionTicketAuthenticationSchemeSelector(schemeName, bearerPrefix));
		services.AddSingleton<ISchemeSelector>(sp =>
			sp.GetRequiredService<SessionTicketAuthenticationSchemeSelector>());
		services.AddSingleton<IBearerSchemeSelector>(sp =>
			sp.GetRequiredService<SessionTicketAuthenticationSchemeSelector>());

		return builder;
	}

}
