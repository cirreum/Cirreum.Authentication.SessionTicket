# Cirreum.Authentication.SessionTicket 1.1.0 — a ticket carries where its subject came from

## Why this release exists

A session ticket re-presents a subject another scheme established. That makes it a
*continuation*: asking what kind of party it authenticates is a category error — whoever called
the negotiate endpoint is who the ticket carries, and that is usually a person but need not be.
An API-key-authenticated service opening a long-lived connection receives a ticket on the same
path.

The attribute-authority model resolves a subject's facts — subject kind, claim authority — from
the scheme that established them. For that to survive the handoff, the ticket has to carry its
origin. This release makes it do so, and tightens the bound principal to assert only what the
ticket actually knows.

## What's new

**Tickets carry and stamp their origin scheme.** `SessionTicketIssueRequest.Scheme` records the
scheme that authenticated the caller at minting (`invocation.Current?.AuthenticatedScheme` in
the negotiate endpoint); the issuer copies it onto the ticket; the handler stamps a validated
ticket's origin into `AuthenticationContextKeys.OriginScheme` so the subject's declaration keeps
resolving from the scheme that actually authenticated them. Optional — a ticket without an
origin resolves `SubjectKind.Unknown`, the fail-safe.

**The scheme declares `SubjectKind.Unknown`.** Registered and declared through the registration
funnel in one act. `Unknown` is the truthful answer, not a placeholder: the origin supplies the
real one at validation.

**The bound principal asserts only what the ticket knows.** `DefaultSessionTicketPrincipalBinder`
seeds `sub` (modern OIDC, replacing the legacy `ClaimTypes.NameIdentifier` / `ClaimTypes.Name`
pair) and `client_type` — and no fabricated `name`. An issuer that knows a display name passes
`name` in the ticket's claims, where it drives `Identity.Name`; pass-through `roles` claims now
actually drive `IsInRole` / `[Authorize(Roles = …)]`, which the docs had always promised. The
non-shadowable set guards the identifier (`sub`, legacy `NameIdentifier`) and `client_type`.

**The documentation tells the whole story.** The README distinguishes the two parallel paths
onto a long-lived connection — authenticated at establishment (the ticket's canonical flow)
versus anonymous then promoted — and their composition, with a worked in-band redemption sample
for the mid-connection upgrade. Negotiate samples enforce authorization (the issuer does not
re-authenticate; the minting endpoint's authorization is the subject proof), seed `Subject` from
`ClaimsHelper.ResolveId`, and carry the origin.

## Compatibility

- **The bound principal's claim shape changed.** Code reading `ClaimTypes.NameIdentifier` or
  `ClaimTypes.Name` off ticket-bound principals must read `sub` / `name`; framework resolution
  (`ClaimsHelper`) handles both. `Identity.Name` is `null` unless the issuer supplied a `name`
  claim — a continuation does not invent a display name.
- **Dropped roadmap surface is removed**: the reserved cookie / query-string transport constants
  (their transports were decided against) — breaking on paper, verified consumer-free, shipped
  as a Minor deliberately. The subprotocol transport remains the one possible future.
- Composition (`AddSessionTicket(...)`), the store contract, and the wire format are untouched.

## See also

- `Cirreum.Kernel 2.1.1` — `AuthenticationContextKeys.OriginScheme`, the slot the handler stamps.
- `Cirreum.Contracts 4.3.0` — `IInvocationContext.AuthenticatedScheme`, the read the negotiate
  samples use.
- `Cirreum.Runtime.AuthenticationProvider` — `connection.Promote(principal, originScheme)`, the
  promotion half of the continuation model.
