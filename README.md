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

`AcceptedTransports` is required — there is no defensible default (the right transport is dictated by the consuming pattern). Pick the transport that matches how the ticket actually flows from your minting endpoint to the consuming handshake:

```csharp
// Browser SPA — minting endpoint sets a cookie; WebSocket upgrade reads it.
builder.Services.AddAuthentication(SessionTicketAuthenticationDefaults.AuthenticationScheme)
    .AddSessionTicket(options => {
        options.AcceptedTransports = CredentialTransport.Cookie;
        options.CookieName = "myapp.session";
    });
```

Startup validation throws a guiding error if `AcceptedTransports` is left at `None`, or if you set a flag that isn't implemented in v1.0 (only `Cookie` ships in 1.0; `BearerAuthorizationHeader`, WebSocket subprotocol, and `QueryString` land in 1.x).

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

    ctx.Response.Cookies.Append("myapp.session", ticket.TicketValue, new CookieOptions {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict,
        MaxAge = TimeSpan.FromMinutes(2)
    });

    return Results.Ok(new { url = "/ws/chat", expiresIn = 120 });
});
```

### Validate at the handshake endpoint

```csharp
app.MapGet("/ws/chat", async ctx => {
    if (!ctx.User.Identity?.IsAuthenticated == true) {
        ctx.Response.StatusCode = 401;
        return;
    }
    // ... upgrade to WebSocket; the connection's principal is bound to the ticket
}).RequireAuthorization();
```

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
| Cookie transport | ✅ | — |
| In-memory `ISessionStore` | ✅ | — |
| Default principal binder | ✅ | — |
| `ISchemeSelector` (`SchemeCategory.SessionEstablishment`) | ✅ | — |
| JWT-variant tickets (RFC 7519 / 8725 / 9068) | — | 1.x |
| Subprotocol transport (`Sec-WebSocket-Protocol`) | — | 1.x |
| Query-string transport | — | 1.x |
| JWT-Bearer transport | — | 1.x |
| Distributed `ISessionStore` (Redis / Cosmos) | — | 1.x |

1.x additions are SemVer-additive — no breaking changes anticipated to the 1.0 surface.

## Two-Phase Auth integration

SessionTicket is the canonical credential for the anonymous-pending-auth scenario: a connection establishes with an unauthenticated sentinel principal, in-band auth steps promote it to a fully authenticated principal via `TwoPhaseAuth.Promote(...)` (in the server spine). Tickets bound at handshake can flow `Channel` and `Reference` annotations through to `IRequestOrigin` for telemetry / audit.

## Security considerations

- **Short TTLs** — Mint tickets with single-digit-minute lifetimes. The v1 hardening posture is short-TTL + single-use + TLS, not DPoP-style sender constraints.
- **Cookie hygiene** — `HttpOnly`, `Secure`, `SameSite=Strict` for browser-bound tickets.
- **Distributed stores** — Multi-head deployments MUST register a distributed `ISessionStore`; the in-memory default does not coordinate across heads.
- **Subject trust** — `SessionTicketIssueRequest.Subject` is the *already-authenticated* subject from the caller's context. The issuer does NOT re-authenticate — callers MUST ensure their `/negotiate` (or equivalent) endpoint requires the authentication that proves the subject.

## License

MIT — see [LICENSE](LICENSE).

---

**Cirreum Foundation Framework**
*Layered simplicity for modern .NET*
