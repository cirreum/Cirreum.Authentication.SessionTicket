# Cirreum.Authentication.SessionTicket 1.0.0 — First-class session-handoff credentials

## Why this release exists

The **Cirreum 1.0 Foundation Reset** recognizes session-handoff as a first-class authentication pattern. SessionTicket is the scheme for it — a credential that bridges the gap between the authentication context that *establishes* a session (a REST `/negotiate` call, a partner webhook, a Twilio inbound flow) and the long-lived connection scope that *uses* it (WebSocket, SignalR, gRPC streaming).

`Cirreum.AuthenticationProvider` defines the contracts; this 1.0 release ships their first concrete implementations.

## What's in 1.0

### Opaque-variant tickets + cookie transport

The v1 hardening posture: short-TTL opaque tickets + single-use + TLS channel security. The package generates 32-byte cryptographically random hex tickets, persists them in an `ISessionStore`, and consumes them on first successful validation. JWT-variant tickets (RFC 7519 / RFC 8725 / RFC 9068) ship in 1.1.0+.

### Composable contracts

The four contracts (`ISessionTicketIssuer`, `ISessionTicketValidator`, `ISessionStore`, `ISessionTicketPrincipalBinder`) live in `Cirreum.AuthenticationProvider` so apps can swap any of them independently:

- Custom claim shape? Register your own `ISessionTicketPrincipalBinder`.
- Distributed deployment? Register a Redis/Cosmos-backed `ISessionStore`.
- App-specific ticket-value generation? Register your own `ISessionTicketIssuer`.
- Different validation semantics (reusable tickets, allow-list per partner)? Your own `ISessionTicketValidator`.

The package's `AddSessionTicket(...)` uses `TryAddSingleton` so any app-side registration wins.

### ISchemeSelector dispatch

The package ships `SessionTicketAuthenticationSchemeSelector` with `SchemeCategory.SessionEstablishment`. The runtime dynamic forward resolver picks SessionTicket when the configured cookie is present on the request.

## Quick start

```csharp
// Composition root
builder.Services.AddAuthentication()
    .AddSessionTicket(options => options.CookieName = "myapp.session");

// Negotiate endpoint — app code mints the ticket
app.MapPost("/negotiate", async (HttpContext ctx, ISessionTicketIssuer issuer) => {
    var ticket = await issuer.IssueAsync(new SessionTicketIssueRequest {
        Subject = ctx.User.Identity!.Name!,
        Lifetime = TimeSpan.FromMinutes(2)
    }, ctx.RequestAborted);

    ctx.Response.Cookies.Append("myapp.session", ticket.TicketValue, new CookieOptions {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict
    });

    return Results.Ok(new { url = "/ws/chat" });
});

// Long-lived endpoint — handler runs at handshake
app.MapGet("/ws/chat", async ctx => {
    /* ... */
}).RequireAuthorization();
```

See [`MIGRATION-v1.md`](MIGRATION-v1.md) for the full pattern walkthrough.

## How it pairs with the rest of the Authentication pillar

| Package | Role |
|---|---|
| `Cirreum.Kernel` | `INotification` markers, versioned-message primitive, auth event bus, `AuthenticationContextKeys` |
| `Cirreum.AuthenticationProvider` | `ISessionTicketIssuer/Validator/Store/PrincipalBinder`, `ISchemeSelector` |
| **`Cirreum.Authentication.SessionTicket`** *(this release)* | Opaque-variant + cookie + in-memory store + scheme handler |
| `Cirreum.Runtime.AuthenticationProvider` | Dynamic forward resolver, selector iteration |
| `Cirreum.Runtime.Authentication` | App-facing `AddAuthentication(...)` builder |

## Compatibility

- **.NET 10.0** target.
- **Cirreum.Providers 1.2.0+** required.
- **Cirreum.AuthenticationProvider 1.0.0+** required.
- No predecessor package — initial release.

## See also

- [`MIGRATION-v1.md`](MIGRATION-v1.md), [`CHANGELOG.md`](CHANGELOG.md)
