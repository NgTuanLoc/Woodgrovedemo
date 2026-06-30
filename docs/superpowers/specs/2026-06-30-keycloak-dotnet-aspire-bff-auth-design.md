# Keycloak + .NET + Aspire Auth Demo — Design Spec

**Date:** 2026-06-30
**Status:** Approved (design), pending implementation plan
**Goal:** A learning project that demonstrates OAuth2 / OIDC / SSO end-to-end using Keycloak, a React 19 frontend, a .NET Backend-For-Frontend (BFF), and a protected .NET API — all orchestrated by .NET Aspire. Ships with a cheatsheet that explains the concepts.

---

## 1. Objectives

1. **Understand** how OAuth2, OIDC, and SSO work in practice (not just toy theory).
2. **Demonstrate** the Authorization Code + PKCE flow with a security-best-practice BFF (tokens never reach the browser).
3. **Run with one command** via Aspire — Keycloak, BFF, API, and React all start together with the realm pre-provisioned.
4. **Produce a cheatsheet** that doubles as study notes.

Non-goals: production hardening, multi-tenant, social login providers, custom Keycloak themes, container deployment to a cloud.

---

## 2. Architecture

```
┌──────────────────────── Aspire AppHost (orchestrator) ────────────────────────┐
│                                                                                 │
│  Keycloak (container)     WebBff (.NET)        Api (.NET)       web (React 19)   │
│  realm-import.json   ◄──► cookie + OIDC   ◄──► JWT bearer  ◄──► Vite dev server  │
│  roles/users/clients      YARP proxy +         role-based       (proxied by BFF) │
│                           token refresh        authorization                    │
└─────────────────────────────────────────────────────────────────────────────────┘
```

**Tier responsibilities (one job each):**

- **Keycloak** — identity provider. Issues ID/access/refresh tokens. Realm provisioned from `realm-export.json`.
- **WebBff** — security core. Performs OIDC login (Auth Code + PKCE) server-side, custodies tokens in an encrypted HttpOnly cookie, refreshes them, and reverse-proxies `/api/*` to the API with a bearer token attached.
- **Api** — resource server. Validates JWTs and enforces role-based authorization.
- **web (React 19)** — UI only. Calls the BFF with cookies; never handles a token.

### Versions / key packages

- **.NET 11** (preview 5 confirmed installed).
- **Aspire** — latest packages: `Aspire.Hosting.AppHost`, `Aspire.Hosting.Keycloak`, Node/Vite app hosting (`Aspire.Hosting.NodeJs` `AddNpmApp`, or Aspire Community Toolkit `AddViteApp` — chosen at plan time based on what's available/stable).
- **Client auth integration:** `Aspire.Keycloak.Authentication` (`AddKeycloakJwtBearer` for the API; `AddKeycloakOpenIdConnect` or the stock OIDC handler for the BFF).
- **BFF proxy:** YARP (`Yarp.ReverseProxy`).
- **Frontend:** React 19 + Vite + TypeScript.
- **Keycloak:** latest official container image (pulled by Aspire).

> Exact version numbers are pinned during planning/implementation against what restores cleanly with .NET 11 preview.

---

## 3. Project layout

```
Woodgrovedemo.sln
src/
  AppHost/                 Aspire orchestrator (entry point: dotnet run)
  ServiceDefaults/         Shared Aspire defaults (telemetry, health, resilience)
  WebBff/                  Cookie + OIDC, YARP proxy, token refresh, BFF endpoints
  Api/                     Minimal API, JWT bearer, role authorization
  web/                     React 19 + Vite + TS frontend
keycloak/
  realm-export.json        Realm: clients, roles, test users (auto-imported)
docs/
  cheatsheet.md            Concept reference / study notes
  superpowers/specs/       This spec
```

---

## 4. Components in detail

### 4.1 AppHost
- Declares Keycloak with `AddKeycloak(...)` + `WithRealmImport("../../keycloak/realm-export.json")`.
- Declares `Api`, `WebBff`, and the React `web` app.
- Wires references so URLs and the Keycloak client secret flow via configuration/service discovery (no hardcoded ports or secrets).
- React app is configured to proxy API/auth calls to the BFF.

### 4.2 ServiceDefaults
- Standard Aspire shared project: OpenTelemetry, health checks, HTTP resilience, service discovery. Referenced by `Api` and `WebBff`.

### 4.3 WebBff (the security core)
- **Authentication:** Cookie (default scheme) + OpenIdConnect (challenge scheme) pointed at the Keycloak realm.
  - `response_type=code`, PKCE enabled, confidential client (`web-bff` + secret), `SaveTokens = true`, appropriate scopes (`openid profile email`, API audience scope, `offline_access` for refresh).
- **Token custody:** tokens stored in the encrypted HttpOnly auth cookie (server-side custody). Never returned to the browser.
- **Token refresh:** implemented in `CookieAuthenticationEvents.OnValidatePrincipal` — when the access token is near expiry, use the refresh token against Keycloak's token endpoint and re-issue the cookie. (Pedagogically explicit; no external token-management dependency required.)
- **Reverse proxy:** YARP route `/api/{**catch-all}` → API, with a transform that attaches `Authorization: Bearer <access_token>` from the stored tokens. Anonymous calls to `/api/*` are rejected by the BFF.
- **BFF endpoints:**
  - `GET /bff/login?returnUrl=` → OIDC challenge.
  - `GET /bff/logout` → clear cookie + redirect to Keycloak end-session endpoint (SSO single logout).
  - `GET /bff/user` → 200 with the current user's claims (JSON) when authenticated, 401 otherwise.
- Serves / proxies the React app (dev: forwards to the Vite dev server).

### 4.4 Api (resource server)
- Minimal API + JWT bearer validation via `AddKeycloakJwtBearer` (issuer = realm, audience = `woodgrove-api`).
- Endpoints:
  - `GET /api/public` — anonymous.
  - `GET /api/me` — `[Authorize]`, echoes caller claims.
  - `GET /api/admin` — `[Authorize(Roles = "admin")]`.
- Role claim mapping configured so Keycloak realm roles land in `ClaimsPrincipal` roles.

### 4.5 web (React 19)
- Vite + TypeScript SPA.
- Auth context bootstraps by calling `GET /bff/user` (cookie-based, `credentials: 'include'`).
- Components:
  - Login / Logout buttons (navigate to `/bff/login` and `/bff/logout`).
  - **Profile / claims viewer** — renders the claims from `/bff/user`.
  - **Role-gated admin section** — visible only when the user has the `admin` role; calls `/api/admin`.
  - **Dev token-inspection panel** — shows decoded token/claim info exposed by a dev-only BFF endpoint (the browser still never receives the raw access token used against the API; the panel is for learning and is gated to development).
- All API calls go to `/api/*` (proxied by the BFF).

### 4.6 Keycloak realm (`realm-export.json`)
- Realm: `woodgrove`.
- Clients:
  - `web-bff` — confidential, Auth Code + PKCE, redirect URIs to the BFF, has client secret.
  - `woodgrove-api` — bearer-only / audience for the API (audience mapper so access tokens carry `aud: woodgrove-api`).
- Realm roles: `admin`, `user`.
- Protocol mapper: realm roles → token (`realm_access.roles`) and a `roles` claim mapped to .NET role claim type.
- Test users: `alice` (role `admin`), `bob` (role `user`) — with known dev passwords documented in the cheatsheet.

---

## 5. Data flow (Auth Code + PKCE via BFF)

1. React calls `GET /bff/user` → 401 → UI shows **Login**.
2. Login → BFF `/bff/login` → 302 to Keycloak authorize endpoint (`response_type=code`, PKCE challenge, scopes).
3. User authenticates at Keycloak → redirect back to BFF with `code`.
4. BFF exchanges `code` + PKCE verifier + client secret at the token endpoint → **ID + access + refresh tokens**; builds claims principal; writes encrypted HttpOnly cookie.
5. React calls `/api/...` → BFF (YARP) attaches `Authorization: Bearer <access_token>` → API.
6. API validates JWT (issuer, audience, signature, expiry) + role check → returns data.
7. Near expiry → BFF `OnValidatePrincipal` refreshes via refresh token transparently.
8. Logout → clear cookie + Keycloak end-session endpoint → **SSO single logout**.

---

## 6. Cheatsheet contents (`docs/cheatsheet.md`)

- OAuth2 roles (resource owner, client, authorization server, resource server) & grant types.
- OIDC vs OAuth2 (authentication vs authorization; the ID token).
- Token types: ID / access / refresh, and JWT anatomy (header.payload.signature, key claims).
- Authorization Code + PKCE flow, with a sequence diagram.
- What SSO is and how it works here (shared Keycloak session, end-session logout).
- BFF pattern: why tokens stay server-side; cookie vs token storage trade-offs.
- Keycloak concepts: realm, client (confidential vs public/bearer-only), client scopes, roles, protocol mappers, audience.
- Aspire concepts used: AppHost, resources, references, realm import, service discovery.
- Key .NET auth APIs: cookie + OIDC handlers, `AddKeycloakJwtBearer`, `[Authorize]`, role mapping.
- Common pitfalls: audience mismatch, redirect URI exactness, HTTPS/dev certs, clock skew, role-claim mapping.
- Handy snippets: discovery doc URL, token endpoint `curl`, decoding a JWT.

---

## 7. Testing

Pragmatic scope for a learning project (global 80% target relaxed here by agreement):

- **API integration tests** — `/api/public` (anon 200), `/api/me` (401 without token, 200 with valid token), `/api/admin` (403 for `user`, 200 for `admin`), using test JWTs.
- **BFF smoke test** — `/bff/login` issues a 302 to Keycloak; `/bff/user` returns 401 when unauthenticated.
- **Manual verification checklist** (in docs): login as alice/bob, view claims, hit admin gate (allowed/denied), observe token refresh, logout clears session.

---

## 8. Open items resolved during planning

- Choice between `AddNpmApp` vs Community Toolkit `AddViteApp` for the React app.
- Exact NuGet versions compatible with .NET 11 preview 5.
- Whether the dev token-inspection endpoint exposes decoded claims only (default) vs raw tokens (dev-gated).

---

## 9. Success criteria

- `dotnet run` on AppHost brings up the Aspire dashboard with Keycloak, BFF, API, and React all healthy.
- A user can log in via Keycloak, see their claims, be correctly allowed/denied the admin section by role, experience transparent token refresh, and log out (SSO single logout).
- Tokens are never observable in browser storage or JS for the API calls.
- The cheatsheet explains every concept the demo exercises.
