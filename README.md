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

---

## Architecture

```
Browser (React 19)
    │  cookie only
    ▼
BFF  (src/WebBff)          ←──── Keycloak (woodgrove realm, port 8080)
    │  OIDC / PKCE login           realm: woodgrove
    │  cookie session              client: web-bff (confidential)
    │  YARP proxy + bearer attach  audience: woodgrove-api
    ▼
API  (src/Api)
    │  JWT Bearer validation
    │  endpoints: /api/public | /api/me | /api/admin
```

### Aspire Resources

| Resource   | Type              | Description                                       |
|------------|-------------------|---------------------------------------------------|
| `keycloak` | Docker container  | Keycloak 26, realm `woodgrove`, port 8080         |
| `api`      | .NET project      | JWT-protected API (`src/Api`)                     |
| `webbff`   | .NET project      | Cookie + OIDC BFF with YARP proxy (`src/WebBff`)  |
| `web`      | npm / Vite        | React 19 SPA dev server (`src/web`)               |

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

Keycloak admin console: `http://localhost:8080` — log in as `admin` / `admin`, select realm `woodgrove`.

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

---

## Running the Tests

### .NET integration tests (5 tests)

```bash
dotnet test
```

Tests in `tests/Api.Tests/`:
- `PublicEndpointTests` — `/api/public` returns 200; `/api/me` returns 401 without a token.
- `ProtectedEndpointTests` — authenticated user gets 200 from `/api/me`; non-admin gets 403 from `/api/admin`; admin gets 200 from `/api/admin`.

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

---

## Cheatsheet

A detailed reference covering OIDC concepts, JWT anatomy, PKCE flow, BFF pattern, Keycloak configuration, and debugging tips:

```
docs/cheatsheet.md
```

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
