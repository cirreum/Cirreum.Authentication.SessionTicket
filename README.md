# Cirreum Authentication - SessionTicket

[![NuGet Version](https://img.shields.io/nuget/v/Cirreum.Authentication.SessionTicket.svg?style=flat-square\&labelColor=1F1F1F\&color=003D8F)](https://www.nuget.org/packages/Cirreum.Authentication.SessionTicket/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Cirreum.Authentication.SessionTicket.svg?style=flat-square\&labelColor=1F1F1F\&color=003D8F)](https://www.nuget.org/packages/Cirreum.Authentication.SessionTicket/)
[![GitHub Release](https://img.shields.io/github/v/release/cirreum/Cirreum.Authentication.SessionTicket?style=flat-square\&labelColor=1F1F1F\&color=FF3B2E)](https://github.com/cirreum/Cirreum.Authentication.SessionTicket/releases)
[![License](https://img.shields.io/badge/license-MIT-F2F2F2?style=flat-square\&labelColor=1F1F1F)](https://github.com/cirreum/Cirreum.Authentication.SessionTicket/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-003D8F?style=flat-square\&labelColor=1F1F1F)](https://dotnet.microsoft.com/)

**Session-handoff authentication for the Cirreum framework**

## Overview

**Cirreum.Authentication.SessionTicket** provides a short-lived credential for handing an already-authenticated subject from one authentication context into a long-lived connection.

A SessionTicket is **not a primary authentication credential**. The user, client, or partner must already have authenticated somewhere else. The ticket carries that established identity across a boundary where the original credential cannot or should not be reused.

The canonical flow is:

1. The caller authenticates to an application endpoint such as `/negotiate`.
2. The application decides whether that caller should be admitted to a connection.
3. `ISessionTicketIssuer` mints a short-lived SessionTicket for the authenticated subject.
4. The ticket is returned to the client or partner.
5. The client presents the ticket when establishing the long-lived connection.
6. SessionTicket validates and consumes the ticket and produces the `ClaimsPrincipal` used by the connection.

Typical targets include WebSocket, SignalR, and gRPC streaming connections.

---

## Choose your authentication path

Before adding SessionTicket, decide **when the connection should become authenticated**.

| Situation                                                                                      | Recommended path                                                                             |
| ---------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| Every request carries its own credential                                                       | **Do not use SessionTicket**                                                                 |
| The caller authenticates before opening a long-lived connection                                | **Mint a SessionTicket and present it at handshake**                                         |
| The connection intentionally starts anonymous and authentication occurs inside that connection | **Use Two-Phase Auth and `connection.Promote(...)`**                                         |
| The connection starts anonymous, but authentication occurs somewhere else                      | **Mint a SessionTicket out-of-band, redeem it in-band, then call `connection.Promote(...)`** |

### The key distinction

SessionTicket solves **credential handoff**.

Two-Phase Auth solves **identity promotion on an existing connection**.

They are related, but neither requires the other.

* A SessionTicket presented at handshake causes the connection to be born authenticated.
* An anonymous connection can be promoted without a SessionTicket if app code already has trustworthy in-band evidence.
* A SessionTicket can also serve as the trustworthy evidence for promotion when authentication occurred out-of-band.

### Do you need SessionTicket?

Use SessionTicket when:

* the application has a long-lived connection, **and**
* the subject was authenticated somewhere other than that connection, **and**
* the original credential is not carried into the connection.

Common examples include:

| Scenario                             | Why SessionTicket                                                                                                                                   |
| ------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| Browser AI chat                      | REST `/negotiate` authenticates and admits the caller; the client presents the resulting ticket on the connection handshake                         |
| Twilio IVA cold-start                | A webhook establishes the authenticated call context; a ticket hands that identity to the worker connection                                         |
| Partner webhook → connection handoff | A partner proves identity to a webhook; a ticket carries that subject into the connection                                                           |
| Mid-connection upgrade               | Authentication completes out-of-band while an anonymous connection is already open; a ticket is redeemed in-band and used to promote the connection |
| Any session-handoff flow             | The context that establishes the subject is different from the scope that consumes it                                                               |

You generally **do not need SessionTicket** for normal request/response APIs. Each request already carries its own authentication credential, so there is no session handoff to perform.

Anonymous → authenticated is also not inherently a SessionTicket concept. For request/response APIs, the client simply sends a credential on the next request. Promotion matters when identity must change on an **already-open long-lived connection**.

### Important lifecycle rule

The default SessionTicket is **single-use**.

One ticket authenticates exactly one handshake or one in-band redemption.

A reconnect therefore requires a **newly minted ticket**. Do not cache a SessionTicket and reuse it across reconnects.

---

## Installation

```bash
dotnet add package Cirreum.Authentication.SessionTicket
```

---

## Quick start

### Compose the scheme

v1.0 supports one transport: an opaque SessionTicket carried as:

```text
Authorization: Bearer <ticket>
```

Register the scheme through `AddSessionTicket(...)`:

```csharp
builder.Services
	.AddAuthentication(...)
	.AddSessionTicket(bearerPrefix: "st_prod_");
```

### Choose a Bearer prefix

`bearerPrefix` controls how SessionTicket participates in Bearer-scheme dispatch.

| Authentication configuration                                                | `bearerPrefix` |
| --------------------------------------------------------------------------- | -------------- |
| SessionTicket is the only provider that probes opaque Bearer credentials    | Optional       |
| Multiple providers may claim Bearer credentials, such as ApiKey or External | **Required**   |

For example:

```csharp
.AddSessionTicket(bearerPrefix: "st_prod_");
```

The prefix is part of the opaque ticket value itself.

If the issuer produces:

```text
st_prod_abc123...
```

that exact value is:

* persisted by the store,
* returned to the client,
* examined by Bearer dispatch,
* presented by the client, and
* validated by the SessionTicket handler.

The prefix is not a wrapper added during transport.

When no prefix is configured, SessionTicket falls back to JWT-shape disambiguation and may claim non-JWT-shaped opaque Bearer values. That fallback is appropriate only when no other Bearer-probing provider can compete for those credentials.

---

## Mint a ticket

Tickets should be minted only after the application has already established and approved the caller.

A negotiate endpoint has three distinct responsibilities:

1. **Authentication** — the authentication pipeline establishes who the caller is.
2. **Admission** — application code decides whether that authenticated caller may open the requested connection.
3. **Handoff** — `ISessionTicketIssuer` creates the credential that carries the approved subject into the connection.

`ISessionTicketIssuer` performs only the third step.

It does **not** authenticate the caller and does **not** decide whether the caller should be admitted.

### Example negotiate endpoint

```csharp
app.MapPost("/negotiate", async (
	IInvocationContextAccessor invocation,
	ISessionTicketIssuer issuer) => {

	var ctx = invocation.Current ?? throw new InvalidOperationException("No invocation context available.");

	// Authentication has already been enforced by RequireAuthorization().
	//
	// This is where the application performs admission decisions:
	// - customer lookup
	// - account enablement
	// - subscription checks
	// - connection limits
	// - application-specific policy
	//
	// Return 403 / ProblemDetails when admission fails.
	// Mint a ticket only for callers the application wants connected.

	var ticket = await issuer.IssueAsync(
		new SessionTicketIssueRequest {
			Subject = ClaimsHelper.ResolveId(ctx.User)!,
			Scheme = ctx.AuthenticatedScheme,
			Lifetime = TimeSpan.FromMinutes(2),
			Channel = "WebChat",
			Reference = ctx.Items["ConversationId"]?.ToString()
		},
		ctx.RequestAborted);

	// TicketValue is the complete opaque credential, including
	// any configured prefix.
	//
	// Return it only over TLS.

	return Results.Ok(new {
		ticket = ticket.TicketValue,
		url = "/ws/chat",
		expiresIn = 120
	});

}).RequireAuthorization();
```

`Channel` and `Reference` are application-defined annotations carried on the validated ticket.
The framework does not interpret them: read them from the ticket for tracing or audit context —
a custom `ISessionTicketPrincipalBinder` can project them onto the principal as claims, or app
code can enrich telemetry with them at bind time.

They are not authorization inputs.

---

## Authenticate at the connection handshake

The client presents the ticket as a Bearer credential:

```http
Authorization: Bearer st_prod_...
```

The SessionTicket authentication handler validates the credential before the endpoint executes:

```csharp
app.MapGet("/ws/chat", async ctx => {

	if (ctx.User.Identity?.IsAuthenticated != true) {
		ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
		return;
	}

	// Upgrade to WebSocket.
	//
	// The connection's ClaimsPrincipal is now bound to the
	// subject established by the SessionTicket.

}).RequireAuthorization();
```

The canonical flow is therefore:

```text
Primary authentication
		|
		v
Authorized /negotiate
		|
		v
Application admission
		|
		v
Issue SessionTicket
		|
		v
Client receives ticket
		|
		v
Connection handshake
		|
		v
Validate + consume ticket
		|
		v
Authenticated connection
```

No promotion occurs in this path. The connection begins authenticated.

---

## Single-use tickets and reconnects

The default validator consumes the SessionTicket on its first successful validation.

That means:

```text
mint
  |
  v
present
  |
  v
validate
  |
  v
consume
```

A second attempt using the same ticket fails.

This applies equally to:

* handshake authentication,
* in-band redemption, and
* concurrent replay attempts.

If a client reconnects, it should return to the application's negotiate flow and obtain a fresh ticket.

Repeatedly attempting to reuse the same SessionTicket usually indicates that the application has missed a mint step in its connection lifecycle.

---

## Mid-connection authentication

Sometimes a long-lived connection intentionally begins anonymous and becomes authenticated later.

Examples include:

* a voice session established before caller authentication,
* a pre-sign-in chat session,
* an anonymous assistant session that later gains account identity,
* authentication completed in a companion browser or SMS flow while the original connection remains open.

Because the connection already exists, authentication cannot change the handshake that created it. Instead, the connection's effective identity is **promoted**.

Cirreum exposes the current long-lived connection through the invocation context:

```csharp
IInvocationContextAccessor.Current?.Connection
```

The pieces involved are:

| Contract                             | Purpose                                                                 |
| ------------------------------------ | ----------------------------------------------------------------------- |
| `IInvocationContextAccessor`         | Ambient access to the current Cirreum invocation                        |
| `IInvocationContext`                 | Describes the invocation currently being processed                      |
| `IInvocationContext.Connection`      | The `IInvocationConnection?` associated with a long-lived invocation    |
| `IInvocationConnection.Promote(...)` | Replaces the connection's effective identity for subsequent invocations, carrying the origin scheme the subject's declaration resolves from |

`IInvocationContextAccessor` is the Cirreum equivalent of ASP.NET's `IHttpContextAccessor`. It is injected through DI and backed by ambient async state, so `Current` remains available through normal `await` continuations during an invocation.

Source adapters establish and clear the accessor at the inbound seam. Application code anywhere within that invocation's async flow can therefore obtain the current connection without carrying it through every method signature.

### Long-lived vs stateless invocations

`IInvocationContext.Connection` is nullable by design.

It is non-null when the invocation belongs to a long-lived connection and null when the source is stateless.

Conceptually:

| Source                                   | `Connection` |
| ---------------------------------------- | ------------ |
| WebSocket / persistent invocation source | Available    |
| SignalR hub invocation                   | Available    |
| Other long-lived Cirreum source          | Available    |
| HTTP request                             | `null`       |
| gRPC unary request                       | `null`       |
| Queue/message handler                    | `null`       |

This is the same architectural distinction described earlier in this README: request/response authentication has no persistent connection identity to change.

If `Connection` is `null`, there is **nothing to promote**.

---

## Promote an existing connection

`Promote(...)` is an extension on `IInvocationConnection` provided by `Cirreum.Runtime.AuthenticationProvider`.

Application code first obtains the connection from the current invocation:

```csharp
var connection = invocationContextAccessor.Current?.Connection;

if (connection is null) {
	// This invocation does not belong to a long-lived connection.
	// There is no connection identity to promote.
	return;
}
```

Once application code has produced a trusted authenticated `ClaimsPrincipal`, promote the connection:

```csharp
connection.Promote(principal, originScheme: "entraExternal");
```

`originScheme` names the scheme that established the subject. The connection carries it so the
subject's facts — subject kind, claim authority — keep resolving from the scheme that actually
authenticated them, not from the transport that now re-presents them. A promotion without an
origin resolves `SubjectKind.Unknown`.

The resulting model is:

```text
Anonymous connection
		|
		v
Invocation begins
		|
		v
Validate authentication evidence
		|
		v
Build authenticated ClaimsPrincipal
		|
		v
Current.Connection.Promote(principal, originScheme)
		|
		v
Connection.EffectiveUser changes
		|
		v
Next invocation observes authenticated identity
```

### Promotion takes effect on the next invocation

Promotion changes the connection's effective identity, not the identity snapshot of the invocation currently executing.

Per-invocation contexts capture the connection's `EffectiveUser` when they are created.

Therefore:

```text
Invocation N begins anonymous
		|
		v
Invocation N calls Promote(...)
		|
		+--> Invocation N remains anonymous
		|
		v
Connection.EffectiveUser is now authenticated
		|
		v
Invocation N+1 is created
		|
		v
Invocation N+1 sees authenticated principal
```

This distinction is intentional.

Code performing the authentication should treat the promotion operation as the boundary between the anonymous and authenticated phases of the connection. Authorization requiring the newly authenticated identity should occur on a **subsequent invocation**, not later in the invocation that performed the promotion.

---

## Promote with application-validated evidence

SessionTicket is not required if application code already has trustworthy authentication evidence inside the connection.

For example:

```csharp
public sealed class AuthenticateOperation(
	IInvocationContextAccessor invocationContextAccessor) {

	public async Task ExecuteAsync(
		AuthenticateRequest request,
		CancellationToken cancellationToken) {

		var connection = invocationContextAccessor.Current?.Connection;

		if (connection is null) {
			// Promotion only applies to long-lived invocation sources.
			return;
		}

		// Custom application code validates the evidence. Validation is scheme-specific —
		// whichever scheme's machinery validated the evidence IS the origin — so the
		// principal and the scheme are returned together rather than the scheme being
		// restated at the call site.
		var evidence = await ValidateAndBuildPrincipalAsync(
			request,
			cancellationToken);

		if (evidence is null) {
			// Authentication failed. Leave the connection unchanged.
			return;
		}

		connection.Promote(evidence.Principal, originScheme: evidence.Scheme);

		// The promoted identity becomes visible beginning with
		// the next invocation on this connection.
	}
}
```

For JWT evidence in a host with more than one audience-routed scheme, the origin need not be
hardcoded inside the validation step either: the framework's audience registrations are
injectable (`IEnumerable<AudienceSchemeRegistration>`), and matching the token's `aud` against
them resolves the same scheme name the request-time selector would have chosen.

Conceptually:

```text
Anonymous connection
		|
		v
Receive in-band authentication evidence
		|
		v
Application validates evidence
		|
		v
Build ClaimsPrincipal
		|
		v
connection.Promote(principal, originScheme)
		|
		v
Next invocation is authenticated
```

This is **Two-Phase Auth without SessionTicket**.

Use it when the authentication evidence can be validated directly within the existing connection.

---

## Promote using a SessionTicket

SessionTicket becomes useful when authentication occurs **outside** the existing connection.

For example:

* a companion browser tab,
* an SMS-linked authentication flow,
* an authenticated REST endpoint,
* a partner webhook,
* another trusted application context.

That external context authenticates the subject and mints a SessionTicket. The anonymous connection later receives that ticket in-band and redeems it.

The flow becomes:

```text
Anonymous connection                     Out-of-band context
		|                                        |
		|                                Authenticate subject
		|                                        |
		|                                Application admission
		|                                        |
		|                                Issue SessionTicket
		|                                        |
		+------------- ticket -------------------+
		|
		v
In-band invocation receives ticket
		|
		v
Validate + consume ticket
		|
		v
Build ClaimsPrincipal
		|
		v
connection.Promote(principal, ticket.Scheme)
		|
		v
Next invocation is authenticated
```

### Complete in-band redemption example

Inject the ambient invocation accessor alongside the SessionTicket validator and principal binder:

```csharp
public sealed class RedeemSessionTicketOperation(
	IInvocationContextAccessor invocationContextAccessor,
	ISessionTicketValidator validator,
	ISessionTicketPrincipalBinder binder) {

	public async Task ExecuteAsync(
		RedeemSessionTicketRequest request,
		CancellationToken cancellationToken) {

		var connection = invocationContextAccessor.Current?.Connection;

		if (connection is null) {
			// This operation was invoked from a stateless source.
			// There is no persistent connection to promote.
			return;
		}

		var ticket = await validator.ValidateAsync(
			request.TicketValue,
			cancellationToken);

		if (ticket is null) {
			// Invalid, expired, or already redeemed.
			// Leave the connection's identity unchanged.
			return;
		}

		var principal = binder.BuildPrincipal(ticket);

		// The ticket's Scheme is the origin — the scheme that established the
		// subject — so the connection keeps resolving the subject's declaration
		// from the scheme that actually authenticated them.
		connection.Promote(principal, originScheme: ticket.Scheme);

		// Promotion updates the connection's EffectiveUser.
		//
		// The current invocation retains the principal it started with.
		// Subsequent invocations on this connection observe the
		// promoted authenticated identity.
	}
}
```

No `Authorization` header and no authentication handler participate in this path.

Application code is explicitly performing the redemption:

```text
ticket value
	|
	v
ISessionTicketValidator
	|
	v
SessionTicket
	|
	v
ISessionTicketPrincipalBinder
	|
	v
ClaimsPrincipal
	|
	v
IInvocationConnection.Promote(...)
```

The validator and binder are the same registered services used by the handshake authentication path.

### Why use the same validator?

The default SessionTicket validator atomically consumes the ticket on successful validation.

That means the same single-use guarantee applies regardless of where redemption occurs:

* handshake authentication,
* in-band promotion, or
* competing concurrent redemption attempts.

A ticket successfully redeemed in-band cannot subsequently authenticate a handshake, and a ticket already consumed at handshake cannot later be used for promotion.

---

## Handshake authentication vs in-band promotion

The important difference is **when the connection receives its identity**.

### SessionTicket at handshake

```text
Authenticate elsewhere
		|
		v
Mint ticket
		|
		v
Connection handshake
		|
		v
Validate ticket
		|
		v
Connection is born authenticated
```

Use this when authentication can complete **before** the long-lived connection is established.

### SessionTicket in-band

```text
Connection is born anonymous
		|
		v
Authenticate elsewhere
		|
		v
Mint ticket
		|
		v
Existing connection receives ticket
		|
		v
Validate ticket
		|
		v
Promote connection
		|
		v
Next invocation is authenticated
```

Use this when the connection must exist **before** authentication completes.

### Direct in-band promotion

```text
Connection is born anonymous
		|
		v
Receive authentication evidence in-band
		|
		v
Application validates evidence
		|
		v
Promote connection directly
		|
		v
Next invocation is authenticated
```

Use this when the existing connection itself receives enough trusted evidence to establish the subject. No SessionTicket handoff is necessary.

The decision can be summarized as:

```text
Does the connection already exist?
		|
   +----+----+
   |         |
  No        Yes
   |         |
   v         v
Ticket at   Where does authentication
handshake   complete?
			 |
		+----+----+
		|         |
	 In-band   Elsewhere
		|         |
		v         v
	 Promote    Ticket
	 directly     +
				Promote
```

---

## Choose a session store

The package includes an in-memory store so simple applications and development environments work without additional infrastructure.

Your deployment topology determines whether that store is appropriate.

| Deployment                                                      | Recommended store                        |
| --------------------------------------------------------------- | ---------------------------------------- |
| Development                                                     | Built-in `InMemorySessionStore`          |
| Single application instance                                     | Built-in store is sufficient             |
| Multiple application instances / horizontally scaled deployment | **Distributed `ISessionStore` required** |

The default SessionTicket model depends on atomic, single-use consumption.

In a multi-head deployment, all application instances must therefore share a store capable of atomically consuming a ticket.

Typical implementations might use Redis, Cosmos, or another distributed persistence mechanism.

An application-supplied `ISessionStore` automatically replaces the built-in default.

---

## Contract surface

The package implements four contracts from `Cirreum.AuthenticationProvider`:

| Contract                        | Default implementation                                                                         | Custom registration                                            |
| ------------------------------- | ---------------------------------------------------------------------------------------------- | -------------------------------------------------------------- |
| `ISessionTicketIssuer`          | `OpaqueSessionTicketIssuer` — 32-byte hex random value + store                                 | App-side `ISessionTicketIssuer` registration wins              |
| `ISessionTicketValidator`       | `OpaqueSessionTicketValidator` — validates and atomically consumes on success                  | Register a custom validator for different redemption semantics |
| `ISessionStore`                 | `InMemorySessionStore` — development / single-head deployment                                  | Register Redis, Cosmos, or another distributed implementation  |
| `ISessionTicketPrincipalBinder` | `DefaultSessionTicketPrincipalBinder` — `sub` + pass-through claims; does not fabricate `name` | Register a custom binder for application-specific claim shapes |

All default registrations use `TryAddSingleton`.

Application registrations therefore win without registration conflicts.

---

## What's in 1.0

| Feature                                                        | 1.0 | Planned         |
| -------------------------------------------------------------- | --- | --------------- |
| Opaque tickets                                                 | ✅   | —               |
| Bearer (`Authorization: Bearer`) transport                     | ✅   | —               |
| In-memory `ISessionStore`                                      | ✅   | —               |
| Single-use validation                                          | ✅   | —               |
| Background expiry sweep                                        | ✅   | —               |
| Default principal binder                                       | ✅   | —               |
| `IBearerSchemeSelector` using `SchemeSelectorPriority.Session` | ✅   | —               |
| `Sec-WebSocket-Protocol` transport                             | —   | Possible future |

The only currently identified transport candidate beyond Bearer is `Sec-WebSocket-Protocol`.

If added, it would be introduced SemVer-additively.

---

## Security considerations

### Use short lifetimes

SessionTickets are Bearer credentials.

Anyone possessing a valid ticket can present it, so tickets should use short, single-digit-minute lifetimes.

The v1 security posture is:

```text
short TTL
	+
single-use
	+
TLS
```

SessionTicket does not currently use DPoP-style sender constraints.

### Require TLS

Opaque tickets travel as Bearer credentials.

Always use HTTPS / TLS.

Never log raw ticket values.

### Treat tickets as single-use

The default validator atomically consumes a ticket on first successful validation.

A stolen ticket replayed after legitimate redemption therefore fails.

Concurrent redemption attempts must also result in at most one successful consumer.

Replacing the default validator with reusable-ticket semantics changes this security property and should be done deliberately.

### Distributed stores must consume atomically

Multi-head deployments **must** register a distributed `ISessionStore`.

The in-memory implementation cannot coordinate redemption across separate application instances.

A distributed `ConsumeAsync` implementation must be atomic.

Examples include mechanisms equivalent to:

* Redis `GETDEL`, or
* a datastore operation that atomically returns and deletes the ticket document.

If consumption is implemented as an independent read followed by delete, two application instances can potentially redeem the same ticket concurrently and the single-use guarantee is lost.

### Do not rely only on store TTL

The validator independently checks the ticket's `ExpiresAt`.

That check is intentional.

Some distributed stores provide best-effort expiration cleanup, meaning an expired document may remain physically readable for some period after its TTL.

Persistence TTL should therefore be treated as cleanup behavior, not as the sole authorization boundary.

### Claims must come from trusted context

`SessionTicketIssueRequest.Claims` are passed through to the resulting principal by the default binder, including role claims.

Construct these claims only from trusted, already-authenticated application context.

Never populate ticket claims directly from unvalidated client input.

The default binder protects framework-owned identifier claims by dropping pass-through values that collide with:

* `sub`,
* legacy `NameIdentifier`, and
* `client_type`.

This prevents ticket claims from replacing the subject identity established by the ticket itself.

A `name` claim may pass through because the default binder does not seed one.

### The subject must already be authenticated

`SessionTicketIssueRequest.Subject` represents the **already-authenticated subject** being handed into another scope.

`ISessionTicketIssuer` does not re-authenticate that subject.

The endpoint calling the issuer must therefore require whatever authentication proves the subject before minting the ticket.

For a typical negotiate flow:

```text
Authenticate
	|
	v
Authorize endpoint
	|
	v
Application admission
	|
	v
Issue ticket
```

Never reverse that relationship by treating possession of access to the issuer as proof of identity.

---

## Architectural summary

A useful way to reason about the package is:

```text
					Where is identity established?
							  |
			 +----------------+----------------+
			 |                                 |
	  Before connection                 After connection
			 |                                 |
			 v                                 v
	Need credential handoff?          Where does auth happen?
			 |                                 |
		+----+----+                     +------+------+
		|         |                     |             |
	   No        Yes                In-band       Out-of-band
		|         |                     |             |
		v         v                     v             v
   Normal API   SessionTicket       Promote       SessionTicket
				at handshake       directly       + Promote
```

Or more simply:

* **SessionTicket** = hand an established identity between authentication scopes.
* **Handshake SessionTicket** = connection starts authenticated.
* **Two-Phase Auth** = connection starts anonymous and is later promoted.
* **SessionTicket + Two-Phase Auth** = authentication happened elsewhere, and the ticket is the evidence used to promote the existing connection.

---

## License

MIT — see [LICENSE](LICENSE).

---

**Cirreum Foundation Framework**  
*Layered simplicity for modern .NET*
