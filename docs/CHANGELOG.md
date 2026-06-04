# Cirreum.Authentication.SessionTicket Changelog

All notable changes to **Cirreum.Authentication.SessionTicket** are documented in this file.

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) — [SemVer](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added

- **Initial release** as part of the **Cirreum 1.0 Foundation Reset** wave. NEW package — session-establishment credentials minted by app code (negotiate endpoints, webhook handlers) and validated at WebSocket / SignalR / gRPC handshake. No predecessor package — the concept lands here in its proper home under the Authentication pillar.
- Concrete implementations of the abstractions (defined in `Cirreum.AuthenticationProvider`):
  - `OpaqueSessionTicketIssuer` — generates cryptographically random 32-byte hex ticket values, persists via `ISessionStore`
  - `OpaqueSessionTicketValidator` — single-use lookup with eviction on first successful validation
  - `InMemorySessionStore` — default `ISessionStore` suitable for dev / single-head deployments
  - `DefaultSessionTicketPrincipalBinder` — sub + name + claims pass-through `ClaimsPrincipal` builder
- ASP.NET Core scheme infrastructure:
  - `SessionTicketAuthenticationHandler` — reads `Authorization: Bearer {ticket}`, validates non-JWT shape, validates via `ISessionTicketValidator`, builds principal
  - `SessionTicketAuthenticationOptions` — accepted transports (must be explicitly set)
  - `SessionTicketAuthenticationSchemeSelector` — `ISchemeSelector` with `SchemeCategory.SessionEstablishment`; probes for non-JWT Bearer presence
  - `SessionTicketAuthenticationDefaults` — scheme name + reserved subprotocol/query constants for 1.x
- Fluent registration via `AuthenticationBuilder.AddSessionTicket(...)` with **mandatory explicit `AcceptedTransports`** — there is no defensible default transport for SessionTicket (the right choice is dictated by the consuming pattern). Startup validation rejects `CredentialTransport.None` and any v1.0-unsupported flag with a guiding error.
- v1.0 supports `CredentialTransport.BearerAuthorizationHeader` only — covers browser SPA (in-memory `access_token` sent as `Authorization: Bearer`) and webhook-handoff (partner returns response-body ticket, resends as Bearer on follow-up calls).

### Bearer-shape disambiguation

The handler and selector both treat JWT-shaped Bearer values (three base64url segments separated by dots) as not-a-SessionTicket and return `NoResult` / `(false, null)` so JWT-Bearer audience selectors can claim them downstream. SessionTicket only accepts opaque bearer values.

### Not yet shipped (planned for 1.x)

- JWT-variant tickets (RFC 7519 / RFC 8725 / RFC 9068 self-contained validation) — currently only opaque-variant ships
- WebSocket subprotocol transport (`Sec-WebSocket-Protocol: cirreum-st.{ticketValue}`) — for two-phase WS auth
- Cookie transport — for server-rendered apps that want HttpOnly cookie sessions
- Query-string transport (signed-download-style)
- Distributed `ISessionStore` (Redis / Cosmos DB) — currently only in-memory

These ship additively in future 1.x minor / patch releases without breaking the 1.0 surface.
