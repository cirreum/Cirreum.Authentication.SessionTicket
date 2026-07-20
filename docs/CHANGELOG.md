# Cirreum.Authentication.SessionTicket Changelog

All notable changes to **Cirreum.Authentication.SessionTicket** are documented in this file.

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) — [SemVer](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Updated

- Updated NuGet packages.

## [1.0.5] - 2026-07-19

### Updated

- Updated NuGet packages.

## [1.0.1] - 2026-07-04

### Updated

- Updated NuGet packages.

## [1.0.0] - 2026-07-03

### Added

- **Initial release** as part of the **Cirreum 1.0 Foundation Reset** wave. NEW package — session-establishment credentials minted by app code (negotiate endpoints, webhook handlers) and validated at WebSocket / SignalR / gRPC handshake. No predecessor package — the concept lands here in its proper home under the Authentication pillar.
- Concrete implementations of the abstractions (defined in `Cirreum.AuthenticationProvider`):
  - `OpaqueSessionTicketIssuer` — generates cryptographically random 32-byte hex ticket values (optionally prefixed), persists via `ISessionStore`
  - `OpaqueSessionTicketValidator` — single-use: atomically consumes the ticket via `ISessionStore.ConsumeAsync` on first successful validation, then re-checks `ExpiresAt` as defense in depth
  - `InMemorySessionStore` — default `ISessionStore` suitable for dev / single-head deployments; atomic `ConsumeAsync`, lazy expiry eviction on read, and a background expiry sweep so minted-but-never-redeemed tickets cannot accumulate unbounded (`IDisposable`)
  - `DefaultSessionTicketPrincipalBinder` — sub + name + claims pass-through `ClaimsPrincipal` builder; drops pass-through claims that collide with the framework-owned identity types (`NameIdentifier`, `Name`, `client_type`) so a ticket cannot spoof the bound subject
- ASP.NET Core scheme infrastructure:
  - `SessionTicketAuthenticationHandler` — reads `Authorization: Bearer {ticket}`, validates the opaque value verbatim via `ISessionTicketValidator`, builds the principal, and advertises `WWW-Authenticate: Bearer` on challenge
  - `SessionTicketAuthenticationOptions` — no configurable members; the Bearer prefix is part of the opaque value (consumed by the selector for dispatch), not a handler option
  - `SessionTicketAuthenticationSchemeSelector` — `IBearerSchemeSelector` at `SchemeSelectorPriority.Session`; claims by prefix match, or (prefix-less) when the Bearer value is non-JWT-shaped
  - `SessionTicketAuthenticationDefaults` — scheme name + reserved cookie / subprotocol / query constants for 1.x
- Fluent registration via `AuthenticationBuilder.AddSessionTicket(string? bearerPrefix = null)`. The optional prefix is the leading segment of the opaque ticket value (Stripe-style `st_prod_…`); it drives the issuer (minting) and the scheme selector (dispatch). A prefix is required when more than one Bearer-probing provider is registered (enforced at boot by `BearerSchemeValidator`); the prefix-less mode falls back to JWT-shape disambiguation and is valid only when SessionTicket is the sole Bearer provider.
- v1.0 supports the `Authorization: Bearer` transport only — covers browser SPA (in-memory `access_token` sent as `Authorization: Bearer`) and webhook-handoff (partner returns response-body ticket, resends as Bearer on follow-up calls).

### Bearer-shape disambiguation

In prefix-less mode the selector treats JWT-shaped Bearer values (three base64url segments separated by dots) as not-a-SessionTicket and returns `(false, null)` so JWT-Bearer audience selectors can claim them downstream. SessionTicket only accepts opaque (non-JWT) bearer values. When a prefix is configured, the prefix alone commits dispatch and shape is irrelevant.

### Not yet shipped (planned for 1.x)

- JWT-variant tickets (RFC 7519 / RFC 8725 / RFC 9068 self-contained validation) — currently only opaque-variant ships
- WebSocket subprotocol transport (`Sec-WebSocket-Protocol: cirreum-st.{ticketValue}`) — for two-phase WS auth
- Cookie transport — for server-rendered apps that want HttpOnly cookie sessions
- Query-string transport (signed-download-style)
- Distributed `ISessionStore` (Redis / Cosmos DB) — currently only in-memory

These ship additively in future 1.x minor / patch releases without breaking the 1.0 surface.
