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
// Composition root
builder.Services.AddAuthentication(SessionTicketAuthenticationDefaults.AuthenticationScheme)
    .AddSessionTicket(options => {
        options.CookieName = "myapp.session";
    });

// Negotiate endpoint — mint the ticket
app.MapPost("/negotiate", async (HttpContext ctx, ISessionTicketIssuer issuer) => {
    var ticket = await issuer.IssueAsync(new SessionTicketIssueRequest {
        Subject = ctx.User.Identity!.Name!,
        Lifetime = TimeSpan.FromMinutes(2),
        Channel = "WebChat"
    }, ctx.RequestAborted);

    // Set the cookie that the WebSocket handshake will carry.
    ctx.Response.Cookies.Append("myapp.session", ticket.TicketValue, new CookieOptions {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict,
        MaxAge = TimeSpan.FromMinutes(2)
    });

    return Results.Ok(new { url = "/ws/chat", expiresIn = 120 });
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

- **Opaque-variant tickets** — cryptographically random 32-byte hex values, persisted via `ISessionStore`
- **Cookie transport** — `Cookie:` header on the handshake request
- **In-memory store** — `InMemorySessionStore` for dev / single-head deployments
- **Default principal binder** — `sub` + `name` + ticket claims pass-through

Apps with distributed deployments register a custom `ISessionStore` (Redis, Cosmos DB, etc.); the package's `TryAddSingleton<ISessionStore, InMemorySessionStore>` registration backs off when an app-supplied store is present.

## What's planned for 1.x

- JWT-variant tickets (self-contained validation; no store lookup)
- Subprotocol transport (`Sec-WebSocket-Protocol: cirreum-st.{value}`)
- Query-string transport (signed-download URLs)
- JWT-Bearer transport
- Distributed-store implementations alongside the in-memory default

These additions are SemVer-additive — no breaking changes anticipated to the 1.0 surface.
