# Cirreum Authentication - SessionTicket

[![NuGet Version](https://img.shields.io/nuget/v/Cirreum.Authentication.SessionTicket.svg?style=flat-square&labelColor=1F1F1F&color=003D8F)](https://www.nuget.org/packages/Cirreum.Authentication.SessionTicket/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Cirreum.Authentication.SessionTicket.svg?style=flat-square&labelColor=1F1F1F&color=003D8F)](https://www.nuget.org/packages/Cirreum.Authentication.SessionTicket/)
[![GitHub Release](https://img.shields.io/github/v/release/cirreum/Cirreum.Authentication.SessionTicket?style=flat-square&labelColor=1F1F1F&color=FF3B2E)](https://github.com/cirreum/Cirreum.Authentication.SessionTicket/releases)
[![License](https://img.shields.io/github/license/cirreum/Cirreum.Authentication.SessionTicket?style=flat-square&labelColor=1F1F1F&color=F2F2F2)](https://github.com/cirreum/Cirreum.Authentication.SessionTicket/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-003D8F?style=flat-square&labelColor=1F1F1F)](https://dotnet.microsoft.com/)

**Session-handoff authentication for the Cirreum framework**

## Overview

**Cirreum.Authentication.SessionTicket** is the SessionTicket authentication scheme. It bridges the gap between the authentication context that establishes a session (a `/negotiate` REST call, a partner webhook, a Twilio inbound flow) and the long-lived connection scope that uses it (WebSocket, SignalR, gRPC streaming).

Apps **mint** tickets via `ISessionTicketIssuer` from their already-authenticated negotiate endpoints or webhook handlers. The minted ticket is delivered to the partner / client. The partner / client **presents** the ticket at handshake. The framework **validates** the ticket and produces a `ClaimsPrincipal` for the long-lived scope.

### When to use SessionTicket

| Scenario | Why SessionTicket |
|---|---|
| Browser AI chat | REST `/negotiate` mints; browser presents cookie at WebSocket upgrade |
| Twilio IVA cold-start | Webhook authorizes the call; ticket presented at the worker's handshake |
| Partner webhook → connection handoff | Partner posts to webhook proving identity; ticket presented at reconnect |
| Any session-handoff flow | When auth context that establishes ≠ scope that uses |

If your app is REST-only (server APIs), you don't need this package — use `Cirreum.Authentication.ApiKey` or `Cirreum.Authentication.SignedRequest` directly.

## Installation

```bash
dotnet add package Cirreum.Authentication.SessionTicket
```

## Quick start

### Compose the scheme

v1.0 ships a single transport — the opaque ticket presented as `Authorization: Bearer`. Compose it through the `AddSessionTicket(...)` verb inside `AddAuthentication`. The optional `bearerPrefix` is the leading, human-recognizable segment of the opaque value (Stripe-style `st_prod_…`); it routes Bearer dispatch and is **required** when more than one Bearer-probing provider (ApiKey, External, …) is registered:

```csharp
builder.Services.AddAuthentication(...)
    .AddSessionTicket(bearerPrefix: "st_prod_");
```

The prefix is part of the opaque ticket value, not a wrapper: the issuer mints, persists, and returns the full prefixed string as one secret, the scheme selector routes on it, and the handler validates it verbatim. Omit `bearerPrefix` only in the single-Bearer-provider case (the selector then falls back to JWT-shape disambiguation, claiming any non-JWT-shaped opaque Bearer value).

### Mint a ticket in your negotiate endpoint

```csharp
app.MapPost("/negotiate", async (HttpContext ctx, ISessionTicketIssuer issuer) => {

    // The caller is already authenticated upstream (e.g., by OIDC bearer).
    var ticket = await issuer.IssueAsync(new SessionTicketIssueRequest {
        Subject = ctx.User.Identity!.Name!,
        Lifetime = TimeSpan.FromMinutes(2),
        Channel = "WebChat",                  // surfaces as IRequestOrigin.Channel
        Reference = ctx.Items["ConversationId"]?.ToString()
    }, ctx.RequestAborted);

    // ticket.TicketValue is the opaque value (prefix included). Return it to the client
    // over TLS; the client presents it as Authorization: Bearer on the handshake call.
    return Results.Ok(new { ticket = ticket.TicketValue, url = "/ws/chat", expiresIn = 120 });
});
```

### Validate at the handshake endpoint

The client sends `Authorization: Bearer st_prod_…`; the scheme authenticates the request before your handler runs:

```csharp
app.MapGet("/ws/chat", async ctx => {
    if (ctx.User.Identity?.IsAuthenticated != true) {
        ctx.Response.StatusCode = 401;
        return;
    }
    // ... upgrade to WebSocket; the connection's principal is bound to the ticket
}).RequireAuthorization();
```

> **Single-use:** the default validator consumes the ticket on first successful validation, so a given ticket authenticates exactly one handshake. Mint a fresh ticket per handshake; reusing one is a smell that usually indicates a missed mint step.

## Contract surface

The package implements the four contracts from `Cirreum.AuthenticationProvider`:

| Contract | Default implementation | Custom registration |
|---|---|---|
| `ISessionTicketIssuer` | `OpaqueSessionTicketIssuer` (32-byte hex random + store) | App-side `ISessionTicketIssuer` registration wins |
| `ISessionTicketValidator` | `OpaqueSessionTicketValidator` (single-use, evicts on hit) | Apps with reusable-ticket semantics register their own |
| `ISessionStore` | `InMemorySessionStore` (dev / single-head) | Multi-head apps register Redis / Cosmos / etc. |
| `ISessionTicketPrincipalBinder` | `DefaultSessionTicketPrincipalBinder` (`sub` + `name` + claims pass-through) | Apps with custom claim shapes register their own |

All registrations use `TryAddSingleton` so app-supplied implementations win without conflict.

## What's in 1.0 vs. coming later

| Feature | 1.0 | Planned |
|---|---|---|
| Opaque-variant tickets | ✅ | — |
| Bearer (`Authorization: Bearer`) transport | ✅ | — |
| In-memory `ISessionStore` (single-use, background expiry sweep) | ✅ | — |
| Default principal binder | ✅ | — |
| `IBearerSchemeSelector` (`SchemeSelectorPriority.Session`) | ✅ | — |
| JWT-variant tickets (RFC 7519 / 8725 / 9068) | — | 1.x |
| Subprotocol transport (`Sec-WebSocket-Protocol`) | — | 1.x |
| Cookie transport | — | 1.x |
| Query-string transport | — | 1.x |
| Distributed `ISessionStore` (Redis / Cosmos) | — | 1.x |

1.x additions are SemVer-additive — no breaking changes anticipated to the 1.0 surface.

## Two-Phase Auth integration

SessionTicket is the canonical credential for the anonymous-pending-auth scenario: a connection establishes with an unauthenticated sentinel principal, in-band auth steps promote it to a fully authenticated principal via `TwoPhaseAuth.Promote(...)` (in the server spine). Tickets bound at handshake can flow `Channel` and `Reference` annotations through to `IRequestOrigin` for telemetry / audit.

## Security considerations

- **Short TTLs** — Mint tickets with single-digit-minute lifetimes. The v1 hardening posture is short-TTL + single-use + TLS, not DPoP-style sender constraints. The ticket is a bearer credential: anyone holding it can present it, so keep the redemption window small.
- **TLS only** — The opaque value travels in `Authorization: Bearer`. Always over HTTPS; never log the raw ticket value.
- **Single-use** — The default validator atomically consumes the ticket on first successful validation, so a stolen-and-replayed ticket fails after the legitimate handshake (and concurrent replays cannot both succeed). Swap in a reusable-ticket validator only with eyes open.
- **Distributed stores** — Multi-head deployments MUST register a distributed `ISessionStore`; the in-memory default does not coordinate across heads. A distributed `ConsumeAsync` MUST be atomic (Redis `GETDEL`, a Cosmos delete-returning-document, etc.) or the single-use guarantee is lost. Don't rely on store TTL alone for expiry — the validator re-checks `ExpiresAt`, but best-effort TTLs (e.g. Cosmos) can leave a lapsed document readable until purge.
- **Claim trust** — `SessionTicketIssueRequest.Claims` flow onto the principal (roles included) via the default binder. Build them from already-authenticated context, never from unvalidated client input. The default binder drops pass-through claims that collide with the framework-owned identity types (`NameIdentifier`, `Name`, `client_type`) so a ticket can't spoof the bound subject.
- **Subject trust** — `SessionTicketIssueRequest.Subject` is the *already-authenticated* subject from the caller's context. The issuer does NOT re-authenticate — callers MUST ensure their `/negotiate` (or equivalent) endpoint requires the authentication that proves the subject.

## License

MIT — see [LICENSE](LICENSE).

---

**Cirreum Foundation Framework**
*Layered simplicity for modern .NET*
