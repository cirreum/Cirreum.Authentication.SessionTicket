# Migration to Cirreum.Authentication.SessionTicket v1.0

**From:** (no predecessor — NEW package)
**To:** `Cirreum.Authentication.SessionTicket 1.0.0`

## Why v1

`Cirreum.Authentication.SessionTicket` is a NEW package in the **Cirreum 1.0 Foundation Reset** wave. The concept of a session-establishment credential — minted by app code at one endpoint, validated at a long-lived-connection handshake elsewhere — lands here in its proper home under the Authentication pillar.

## Nothing to migrate from

This is an initial release. No prior `Cirreum.Authorization.SessionTicket` or similar package existed.

## When to add this package

Install `Cirreum.Authentication.SessionTicket` when your app needs one of:

- **Browser AI chat** — REST `/negotiate` mints a ticket, browser presents it at WebSocket handshake
- **Twilio IVA cold-start** — webhook authorizes the call session and mints a ticket; the IVA worker validates at its connection handshake
- **Partner webhook → connection handoff** — partner posts to a webhook with their proven identity; framework mints a ticket; partner reconnects with the ticket
- **Any session-handoff flow** where the authentication context that *establishes* the session is different from the long-lived connection scope that *uses* it

If your scheme is server-API-only (REST + ApiKey, REST + SignedRequest), you do not need this package.

## Quick start

```csharp
// Composition root — the prefix is the leading segment of the opaque value (st_prod_…)
builder.Services.AddAuthentication(SessionTicketAuthenticationDefaults.AuthenticationScheme)
    .AddSessionTicket(bearerPrefix: "st_prod_");

// Negotiate endpoint — mint the ticket
app.MapPost("/negotiate", async (HttpContext ctx, ISessionTicketIssuer issuer) => {
    var ticket = await issuer.IssueAsync(new SessionTicketIssueRequest {
        Subject = ctx.User.Identity!.Name!,
        Lifetime = TimeSpan.FromMinutes(2),
        Channel = "WebChat"
    }, ctx.RequestAborted);

    // Return the opaque value (prefix included) to the client over TLS; it presents it
    // as Authorization: Bearer on the handshake call.
    return Results.Ok(new { ticket = ticket.TicketValue, url = "/ws/chat", expiresIn = 120 });
});

// Long-lived endpoint — handler runs at handshake, validates the ticket.
app.MapGet("/ws/chat", async ctx => {
    if (ctx.User.Identity?.IsAuthenticated != true) {
        ctx.Response.StatusCode = 401;
        return;
    }
    // ... upgrade to WebSocket
}).RequireAuthorization();
```

## What ships in 1.0

- **Opaque-variant tickets** — cryptographically random 32-byte hex values (optionally prefixed), persisted via `ISessionStore`, single-use via atomic `ConsumeAsync`
- **Bearer transport** — opaque value presented as `Authorization: Bearer`
- **In-memory store** — `InMemorySessionStore` for dev / single-head deployments (atomic consume + background expiry sweep)
- **Default principal binder** — `sub` + `name` + ticket claims pass-through (framework-owned identity claims protected from shadowing)

Apps with distributed deployments register a custom `ISessionStore` (Redis, Cosmos DB, etc.); the package's `TryAddSingleton<ISessionStore, InMemorySessionStore>` registration backs off when an app-supplied store is present.

## What's planned for 1.x

- JWT-variant tickets (self-contained validation; no store lookup)
- Subprotocol transport (`Sec-WebSocket-Protocol: cirreum-st.{value}`)
- Cookie transport (`HttpOnly` cookie on the handshake request)
- Query-string transport (signed-download URLs)
- Distributed-store implementations alongside the in-memory default

These additions are SemVer-additive — no breaking changes anticipated to the 1.0 surface.
