# Woodgrove Demo

A working reference implementation of the **BFF (Backend-for-Frontend) security pattern** combining Keycloak, React 19, .NET BFF, and a JWT-protected API — all orchestrated by .NET Aspire with a single command.

---

## What This Demonstrates

- Authorization Code + PKCE flow — tokens never reach the browser
- BFF cookie session (`Woodgrove.Bff`, HttpOnly, SameSite=Lax)
- YARP reverse-proxy with automatic bearer-token attachment
- Transparent token refresh (silent — no browser redirect)
- Role-based access control: `admin` vs `user`
- Full Keycloak realm auto-import via Aspire
- Real SSO: a second app (`intranet` client) logs in silently — no password prompt
- Single logout via OIDC Back-Channel Logout — logout in either app ends both sessions

---

## Architecture

```
Browser (React 19)                       Browser (Razor Pages)
    │  cookie: Woodgrove.Bff                 │  cookie: Woodgrove.Intranet
    ▼                                        ▼
BFF  (src/WebBff)  ◀──── Keycloak ────▶  Intranet (src/Intranet)
    │  OIDC/PKCE, YARP     woodgrove realm   │  OIDC/PKCE, SSO demo
    │  bearer attach       clients: web-bff, │
    ▼                      intranet          │
API  (src/Api)             back-channel logout_token POSTs to both apps
```

### Aspire Resources

| Resource   | Type              | Description                                       |
|------------|-------------------|---------------------------------------------------|
| `keycloak` | Docker container  | Keycloak 26, realm `woodgrove` (HTTPS, Aspire-assigned port — see dashboard) |
| `api`      | .NET project      | JWT-protected API (`src/Api`)                     |
| `webbff`   | .NET project      | Cookie + OIDC BFF with YARP proxy (`src/WebBff`)  |
| `web`      | npm / Vite        | React 19 SPA dev server (`src/web`)               |
| `intranet` | .NET project      | Razor Pages second OIDC client — SSO + back-channel logout demo (`src/Intranet`, http://localhost:5262) |

---

## Prerequisites

| Requirement        | Version          | Notes                                          |
|--------------------|------------------|------------------------------------------------|
| Docker             | any recent       | Must be running before `dotnet run`            |
| .NET SDK           | 11 preview 5+    | Required for Aspire and the .NET projects      |
| Node.js / npm      | 20+              | Required for the React Vite dev server         |

---

## Running the Demo

```bash
dotnet run --project src/AppHost
```

Aspire starts all four resources in dependency order: Keycloak first, then API and BFF (which wait for Keycloak to be healthy), then the React dev server. Open the Aspire dashboard URL printed to the console to monitor all resources.

---

## Test Users

| Username | Password   | Roles          |
|----------|-----------|----------------|
| `alice`  | `password` | `admin`, `user` |
| `bob`    | `password` | `user`          |

Keycloak admin console: this Aspire setup serves Keycloak over **HTTPS on an Aspire-assigned (dynamic) port** (not `http://localhost:8080`). Open the **`keycloak` resource in the Aspire dashboard** and click its endpoint link, then log in as **`admin` / `admin`** (pinned in `src/AppHost/Program.cs`) and select realm `woodgrove`. Accept the dev-cert warning.

> `dev-intranet-secret` (like `dev-bff-secret`) is a hardcoded **dev-only** client secret for the realm import. Production: real secret management, exact redirect URIs, HTTPS back-channel URLs.

---

## Key Endpoints

| Endpoint              | Auth required | Notes                                  |
|-----------------------|---------------|----------------------------------------|
| `GET /api/public`     | No            | Anonymous                              |
| `GET /api/me`         | Yes           | Returns name + claims                  |
| `GET /api/admin`      | Yes, `admin`  | 403 for `user`-only accounts           |
| `GET /bff/login`      | —             | Triggers OIDC challenge                |
| `GET /bff/logout`     | —             | Deletes cookie + Keycloak end-session  |
| `GET /bff/user`       | —             | 200 with user JSON or 401              |
| `GET /bff/debug/tokens` | —           | Decoded tokens (development only)      |
| `GET /auth/login` (intranet)      | —    | Triggers OIDC challenge (silent if SSO session exists) |
| `GET /auth/logout` (intranet)     | —    | Single logout — ends both apps' sessions |
| `POST /bff/backchannel-logout`    | —    | Keycloak-only: receives signed logout tokens |
| `POST /auth/backchannel-logout`   | —    | Keycloak-only: receives signed logout tokens |

---

## Running the Tests

### .NET tests

```bash
dotnet test
```

- `tests/Api.Tests` — public/401 + role-based endpoint tests (5).
- `tests/AuthShared.Tests` — logout-token validation, sid denylist, returnUrl sanitizer.
- `tests/WebBff.Tests` — BFF smoke tests + back-channel logout integration (4).
- `tests/Intranet.Tests` — Intranet smoke tests + back-channel logout integration (4).

### React unit tests (2 tests)

```bash
cd src/web && npx vitest run
```

Tests in `src/web/src/auth/AuthContext.test.tsx`:
- Shows authenticated user when `/bff/user` returns 200.
- Shows anonymous state when `/bff/user` returns 401.

---

## Manual Verification Checklist

Run `dotnet run --project src/AppHost` and verify the following in a browser:

- [ ] **Dashboard healthy** — Aspire dashboard shows `keycloak`, `api`, `webbff`, and `web` all in a healthy/running state.
- [ ] **Unauthenticated guard** — Navigate to `GET /bff/user` (or click the endpoint link in the React app before logging in); response is `401 Unauthorized`.
- [ ] **Login as alice** — Click the Login button in the React app; log in with `alice` / `password`; profile page shows name `alice` and role `admin`.
- [ ] **Admin access (alice)** — The Admin section/page loads successfully and `/api/admin` returns 200 for alice.
- [ ] **Admin denied (bob)** — Log out, log in as `bob` / `password`; navigating to the Admin section returns `403 Forbidden` (the UI shows the access-denied state).
- [ ] **Token inspector** — Open the Token Panel (dev tool in the React app); the decoded access token shows `"aud": "woodgrove-api"` and `"roles": ["admin", "user"]` (for alice).
- [ ] **Transparent token refresh** — Wait for the access token to expire (5-minute TTL); continue using the app; the session remains active without a login prompt (the BFF silently refreshes using the refresh token).
- [ ] **Logout clears session** — Click Logout; verify the app returns to the anonymous state; `GET /bff/user` returns `401` again.
- [ ] **Silent SSO login** — Log in on the React app, open http://localhost:5262, click "Log in": you are signed in with no password prompt.
- [ ] **Single logout (Intranet → React)** — Log out on the Intranet; reload the React app: logged out. `webbff` logs show "Back-channel logout: revoked Keycloak session".
- [ ] **Single logout (React → Intranet)** — Log back in, log out from the React app; reload the Intranet: logged out. `intranet` logs show the revocation line.

---

## Cheatsheet

A detailed reference covering OIDC concepts, JWT anatomy, PKCE flow, BFF pattern, Keycloak configuration, and debugging tips:

[docs/cheatsheet.md](docs/cheatsheet.md)

---

## Source Map

| File                                  | Purpose                                    |
|---------------------------------------|--------------------------------------------|
| `src/AppHost/Program.cs`              | Aspire orchestration                       |
| `src/WebBff/Program.cs`               | BFF: Cookie + OIDC + YARP config           |
| `src/WebBff/TokenRefresher.cs`        | Silent token refresh hook                  |
| `src/Api/Program.cs`                  | API: JWT bearer + endpoint definitions     |
| `src/Api/RolesClaimsHelper.cs`        | Claim-type constants (`roles`, username)   |
| `src/web/vite.config.ts`              | Vite proxy + Aspire port wiring            |
| `src/web/src/api/client.ts`           | `apiGet` helper (credentials: include)     |
| `src/web/src/auth/AuthContext.tsx`     | React auth context + `/bff/user` polling   |
| `keycloak/woodgrove-realm.json`       | Realm, client, users, roles, mappers       |
| `tests/Api.Tests/`                    | .NET integration tests (5 tests)           |
| `src/web/src/auth/AuthContext.test.tsx` | React unit tests (2 tests)               |
| `src/Intranet/Program.cs`             | Second OIDC client (SSO + single logout demo) |
| `src/AuthShared/`                     | Logout-token validator, sid denylist, back-channel endpoint |
