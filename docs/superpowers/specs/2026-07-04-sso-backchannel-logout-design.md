# Second App for Real SSO + Back-Channel Single Logout — Design Spec

**Date:** 2026-07-04
**Status:** Approved (design), pending implementation plan
**Goal:** Close the biggest gap in the original demo: SSO is *explained* in the cheatsheet but never *demonstrated*. Add a second OIDC client app so silent SSO login is observable, and implement OIDC Back-Channel Logout in both directions so logging out of either app ends the session in both.

Builds on: `2026-06-30-keycloak-dotnet-aspire-bff-auth-design.md` (implemented).

---

## 1. Objectives

1. **Demonstrate silent SSO login** — log into app #1, then log into app #2 without being prompted for credentials.
2. **Demonstrate single logout, both directions** — logout in either app terminates the Keycloak SSO session *and* the other app's local cookie session, via OIDC Back-Channel Logout.
3. **Close the BFF smoke-test gap** from the original spec §7 (`/bff/user` 401, `/bff/login` 302) while adding the new BFF endpoint tests.
4. **Upgrade the cheatsheet** SSO section from theory to "how it works here", with a back-channel logout sequence diagram.

Non-goals: front-channel logout, distributed session stores, production hardening, more than two apps.

---

## 2. Key decisions (made during brainstorming)

| Decision | Choice | Why |
|---|---|---|
| Shape of app #2 | Server-rendered **Razor Pages** app ("Woodgrove Intranet") | Minimal moving parts; contrasts with the SPA+BFF; shows SSO across app styles |
| Logout propagation | **Back-channel logout** (signed `logout_token` POSTed server-to-server) | The production-grade mechanism; works without browser cooperation; most instructive |
| Direction | **Both directions** — both apps receive and act on logout tokens | Complete lesson |
| Session revocation mechanism | **`sid` denylist + `OnValidatePrincipal` cookie event** | ~150 lines, pedagogically transparent, composes with the existing `TokenRefresher`; lazy revocation (next request) is the standard behavior |
| Rejected alternatives | `ITicketStore` server-side sessions (more code, reworks BFF token custody); Duende.BFF (replaces the hand-built BFF this project exists to teach; licensing) | |

---

## 3. Architecture

```
Browser ──cookie──▶ WebBff  ◀─OIDC─▶  Keycloak  ◀─OIDC─▶ Intranet ◀──cookie── Browser
         (React SPA)   ▲               realm: woodgrove        (Razor Pages)
                       │               clients: web-bff,
                       │                        intranet
                       └──── back-channel logout_token POSTs ──┘
```

- New Aspire resource `intranet` (net11.0 Razor Pages project, `src/Intranet`).
- New confidential Keycloak client `intranet`, secret `dev-intranet-secret` (**DEV ONLY**, same caveat as `dev-bff-secret`).
- Same realm (`woodgrove`), same users (`alice`, `bob`) — the shared Keycloak SSO session is what makes silent login work.

**Deployment wrinkle — back-channel URLs:** Keycloak (container) must POST logout tokens *to* the apps (host processes), and those URLs are static strings in the realm import. Therefore:

- `webbff` and `intranet` each get a **pinned HTTP port** in AppHost (exact ports chosen at plan time against current `launchSettings.json`).
- Each Keycloak client registers `backchannel.logout.url` = `http://host.docker.internal:<pinned-port>/<backchannel-path>` and `backchannel.logout.session.required` = `true`.
- HTTP (not HTTPS) is used for the container→host hop because the ASP.NET dev cert is not trusted inside the container. Flagged as a dev-only simplification.

---

## 4. Components

### 4.1 `src/Intranet` (new)

- Razor Pages, cookie auth (`Woodgrove.Intranet`, HttpOnly, SameSite=Lax) + OIDC challenge (`AddKeycloakOpenIdConnect`, client `intranet`, Auth Code + PKCE, `RequireHttpsMetadata = false` dev-only).
- Deliberately lean: it calls no API → no `SaveTokens` custody, no token refresher. Its one job is demonstrating SSO.
- Must retain the **`sid` claim** from the ID token in the principal (add a claim action if the default mapping drops it) — `sid` is the session handle logout tokens reference.
- Pages/endpoints:
  - `Index` — public; shows auth state, name, roles, claims; a "this login happened via SSO — no password prompt" callout when applicable; link to the React app.
  - `GET /auth/login` — OIDC challenge (returnUrl-validated like the BFF's).
  - `GET /auth/logout` — clears cookie + RP-initiated logout at Keycloak end-session.
  - `POST /auth/backchannel-logout` — via shared `MapBackchannelLogout`.

### 4.2 `src/AuthShared` (new class library)

Shared back-channel machinery (~150 lines), consumed by `WebBff` and `Intranet`:

- **`LogoutTokenValidator`** — validates the `logout_token` JWT per the OIDC Back-Channel Logout 1.0 spec:
  - signature via the realm's JWKS, obtained through the app's existing OIDC `ConfigurationManager`;
  - `iss` = realm issuer; `aud` contains own client id; `iat` sane;
  - `events` claim present and containing `http://schemas.openid.net/event/backchannel-logout`;
  - `sid` present (we register `backchannel.logout.session.required = true`);
  - **reject if `nonce` is present** (spec prohibition).
  - Returns the validated `sid` or a failure reason.
- **`SessionDenylist`** — `IMemoryCache`-backed revoked-`sid` set; entry TTL = cookie lifetime.
- **`MapBackchannelLogout(path)`** — endpoint extension: reads `logout_token` from the form body, validates, denylists the `sid`; `200` empty body on success, `400` on any failure (reason logged, never echoed).
- **Cookie-event helper** — checks the incoming principal's `sid` against the denylist and calls `RejectPrincipal()` on a hit; designed to compose with an existing `OnValidatePrincipal` delegate.

### 4.3 `src/WebBff` (modified)

- Maps `POST /bff/backchannel-logout` via the shared extension.
- `OnValidatePrincipal` chain becomes: **denylist check first**, then the existing `TokenRefresher`.
- Ensures the `sid` claim survives into the cookie principal.

### 4.4 `keycloak/woodgrove-realm.json` (modified)

- Adds client `intranet` (confidential, standard flow, PKCE, `redirectUris`/`webOrigins` `*` dev-only like `web-bff`, realm-roles mapper reused via `fullScopeAllowed`).
- Adds `backchannel.logout.url` + `backchannel.logout.session.required` attributes to **both** clients.

### 4.5 AppHost + cross-links

- AppHost registers `intranet` with `WithReference(keycloak)` / `WaitFor(keycloak)` and the pinned HTTP endpoint; `webbff` HTTP endpoint pinned likewise.
- Each app links to the other (React app gets an "Open Intranet" link; Intranet links back) so the SSO walk-through is one click. URL wiring mechanism (env var vs pinned-port constant) decided at plan time.

---

## 5. Data flows

### 5.1 Silent SSO login

1. User logs into the React app as `alice` (existing flow) → Keycloak now holds an SSO session cookie in the browser.
2. User opens the Intranet → clicks "Log in" → 302 to Keycloak authorize endpoint.
3. Keycloak sees its SSO session cookie → **no credential prompt** → immediately redirects back with a fresh authorization code.
4. Intranet exchanges the code, builds its own principal, issues `Woodgrove.Intranet` cookie.
5. Result: two apps, two independent cookies, one login.

### 5.2 Back-channel single logout (either direction)

1. User clicks logout in app A → app A clears its own cookie and performs RP-initiated logout at Keycloak's end-session endpoint.
2. Keycloak terminates the SSO session and POSTs a signed `logout_token` (form field `logout_token`) to every session participant's registered back-channel URL.
3. App B's back-channel endpoint validates the token and adds its `sid` to the denylist → `200`.
4. On app B's next request, the cookie event finds the `sid` denylisted → `RejectPrincipal()` → user is anonymous there too.

Lazy revocation (step 4 happens on the *next* request) is inherent to the mechanism and called out in the cheatsheet.

---

## 6. Error handling

- Back-channel endpoint: malformed/invalid token, JWKS fetch failure, missing `sid`, `nonce` present → `400`, reason logged server-side only. Valid → `200` empty body.
- Denylist entries expire with the cookie lifetime (8h) to bound memory. In-memory single-instance scope is a documented dev simplification (production: distributed cache).
- Principal without a `sid` claim (e.g. cookie issued before this change) → treated as not denylisted; documented as a pitfall.
- `returnUrl` on Intranet login validated as local, same as the BFF (no open redirect).

---

## 7. Testing

- **`tests/AuthShared.Tests`** — logout-token validation with locally signed test JWTs and a fixed test JWKS: valid → `sid` extracted; wrong `aud`, wrong `iss`, missing `events`, `nonce` present, bad signature → each rejected. `SessionDenylist` add/contains/expiry.
- **`tests/WebBff.Tests`** (new; also closes original spec §7 gap) — `WebApplicationFactory` smoke tests: `/bff/user` → 401 unauthenticated; `/bff/login` → 302 whose `Location` points at Keycloak; `POST /bff/backchannel-logout` with garbage → 400.
- **`tests/Intranet.Tests`** — same smoke pattern (public Index 200, login 302, back-channel garbage 400).
- **Manual verification additions** (README checklist):
  - Log in on React app, then Intranet login requires **no credentials**.
  - Logout on Intranet → React app is logged out on next interaction.
  - Logout on React app → Intranet is logged out on next interaction.

---

## 8. Documentation

- **Cheatsheet:** SSO section rewritten from "how a second app would log in" to "how the Intranet logs in here"; new Back-Channel Logout section with sequence diagram, logout-token anatomy (`events`, `sid`, no `nonce`), and pitfalls (host.docker.internal URL reachability, lazy revocation, in-memory denylist scope).
- **README:** `intranet` resource row, updated architecture diagram, new checklist items, `dev-intranet-secret` dev-only caveat.

---

## 9. Success criteria

- `dotnet run --project src/AppHost` brings up all five resources healthy (`keycloak`, `api`, `webbff`, `web`, `intranet`).
- Silent SSO login observable: second app logs in with no credential prompt.
- Single logout works in **both** directions via back-channel logout tokens.
- All existing tests still pass; new AuthShared/WebBff/Intranet test projects pass.
- Cheatsheet and README updated so every demonstrated concept is explained.
