# OAuth2 / OIDC / SSO Cheatsheet
## Woodgrove Demo Study Notes

> Concrete values from **this** project so abstract concepts map to real files you can open.

---

## 1. OAuth2 Roles & Grant Types

### The Four OAuth2 Roles

| Role | Generic Name | In This Project |
|------|-------------|-----------------|
| **Resource Owner** | The user who owns the data | `alice` or `bob` (password: `password`) |
| **Client** | App requesting access on behalf of the user | BFF (`web-bff` client in Keycloak) |
| **Authorization Server** | Issues tokens after the user authenticates | Keycloak — realm `woodgrove`, port `8080` |
| **Resource Server** | Hosts the protected data/API | `src/Api` — validates `Authorization: Bearer <token>` |

### Grant Types

| Grant Type | Use Case | Used Here? |
|-----------|---------|------------|
| **Authorization Code + PKCE** | Web apps, SPAs with a server component | Yes — BFF uses this |
| **Client Credentials** | Machine-to-machine (no user) | No (would use `serviceAccountsEnabled: true`) |
| **Device Code** | Devices without a browser (smart TV, CLI) | No |
| **Implicit** | Deprecated — tokens in URL fragment | No — never use |
| **Password / ROPC** | Dev/test only — send credentials directly | Disabled (`directAccessGrantsEnabled: false` in `woodgrove-realm.json`) |

---

## 2. OIDC vs OAuth2

| | OAuth2 | OIDC (OpenID Connect) |
|---|--------|----------------------|
| **Purpose** | Authorization — "can this app access resource X?" | Authentication — "who is this user?" |
| **Built on** | — | OAuth2 (adds identity layer) |
| **Token issued** | Access token (opaque or JWT) | ID token (always JWT) + access token |
| **Standard claims** | `iss`, `aud`, `exp` (minimal) | `sub`, `name`, `email`, `preferred_username`, etc. |
| **Discovery endpoint** | None | `/.well-known/openid-configuration` |

OIDC is OAuth2 **plus** an identity layer. When you request scope `openid`, Keycloak issues both an **ID token** (who the user is) and an **access token** (what the app can do).

### The Three Token Types

| Token | Purpose | Where it lives in this project |
|-------|---------|-------------------------------|
| **ID Token** | Proves identity to the client | Stored server-side (encrypted in an HttpOnly cookie; readable only server-side, never by JavaScript) in `Woodgrove.Bff` cookie properties via `SaveTokens = true`; accessible via `/bff/debug/tokens` (dev only) |
| **Access Token** | Sent to the API as `Bearer` proof | Also in cookie properties; YARP attaches it to every proxied request |
| **Refresh Token** | Exchange for a new access token when it expires | In cookie properties; `TokenRefresher.cs` uses it automatically |

The access token lifespan in this project is **300 seconds** (5 minutes), set in `woodgrove-realm.json` (`"accessTokenLifespan": 300`). `TokenRefresher.cs` refreshes when fewer than **1 minute** remain.

### JWT Anatomy

A JWT is three Base64url-encoded parts separated by dots:

```
header.payload.signature
```

**Header** — algorithm metadata:
```json
{
  "alg": "RS256",
  "typ": "JWT",
  "kid": "<key-id>"
}
```

**Payload** — claims. Example access token from this project:
```json
{
  "iss": "http://localhost:8080/realms/woodgrove",
  "aud": ["woodgrove-api", "account"],
  "sub": "a1b2c3d4-...",
  "preferred_username": "alice",
  "email": "alice@example.com",
  "roles": ["admin", "user"],
  "exp": 1735000000,
  "iat": 1734999700
}
```

**Key claims in this project:**

| Claim | Meaning | This Project |
|-------|---------|-------------|
| `iss` | Issuer — who created the token | `http://localhost:8080/realms/woodgrove` |
| `aud` | Audience — intended recipient | `woodgrove-api` (added by `audience-woodgrove-api` protocol mapper) |
| `sub` | Subject — stable user identifier (UUID) | User's Keycloak UUID |
| `preferred_username` | Human-readable username | `alice` or `bob` |
| `roles` | Custom flat claim — realm roles | `["admin","user"]` for alice, `["user"]` for bob |
| `exp` | Expiry timestamp (Unix epoch) | `iat + 300` |
| `iat` | Issued-at timestamp | When Keycloak issued the token |

**Signature** — `RS256(base64url(header) + "." + base64url(payload), private_key)`. The API verifies this using Keycloak's public key fetched from the JWKS endpoint.

---

## 3. Authorization Code + PKCE Flow

PKCE (Proof Key for Code Exchange) prevents authorization code interception attacks. The client generates a `code_verifier` (random string) and sends `code_challenge = BASE64URL(SHA256(ASCII(code_verifier)))` upfront (the challenge is the base64url-encoded SHA-256 digest of the verifier; the raw hash bytes are never sent); only the holder of the original `code_verifier` can exchange the code.

```mermaid
sequenceDiagram
    participant B as Browser (React)
    participant BFF as BFF (src/WebBff)
    participant KC as Keycloak (woodgrove)
    participant API as API (src/Api)

    Note over B,KC: 1. Login initiation
    B->>BFF: GET /bff/login
    BFF->>BFF: Generate code_verifier + code_challenge (PKCE)
    BFF-->>B: 302 Redirect → Keycloak /authorize?<br/>client_id=web-bff<br/>&response_type=code<br/>&scope=openid profile email roles offline_access<br/>&code_challenge=<SHA256><br/>&redirect_uri=<bff-callback>

    Note over B,KC: 2. User authenticates at Keycloak
    B->>KC: GET /authorize (redirect)
    KC-->>B: Show login form
    B->>KC: POST credentials (alice / password)
    KC-->>B: 302 Redirect → BFF callback?code=<auth_code>&state=...

    Note over BFF,KC: 3. BFF exchanges code for tokens (back-channel)
    B->>BFF: GET /signin-oidc?code=<auth_code>&state=...
    BFF->>KC: POST /realms/woodgrove/protocol/openid-connect/token<br/>grant_type=authorization_code<br/>&code=<auth_code><br/>&code_verifier=<original><br/>&client_id=web-bff<br/>&client_secret=dev-bff-secret
    KC-->>BFF: { id_token, access_token, refresh_token, expires_in }

    Note over BFF,B: 4. BFF stores tokens & issues cookie
    BFF->>BFF: SaveTokens=true → store tokens in session
    BFF-->>B: 302 Redirect / + Set-Cookie: Woodgrove.Bff=<encrypted> HttpOnly

    Note over B,API: 5. Subsequent API calls through BFF proxy
    B->>BFF: GET /api/me (Cookie: Woodgrove.Bff)
    BFF->>BFF: Read access_token from cookie properties
    BFF->>API: GET /api/me + Authorization: Bearer <access_token>
    API->>API: Validate JWT (audience=woodgrove-api, signature)
    API-->>BFF: { name: "alice", claims: [...] }
    BFF-->>B: { name: "alice", claims: [...] }
```

**Key points:**
- The `code_verifier`/`code_challenge` exchange happens automatically via `options.UsePkce = true` in `src/WebBff/Program.cs`.
- The auth code is short-lived and single-use — interception is useless without the `code_verifier`.
- Tokens **never reach the browser**. The browser only holds the `Woodgrove.Bff` HttpOnly cookie.

---

## 4. SSO & Single Logout

### How SSO Works (demonstrated by the Intranet app)

Keycloak maintains a **server-side SSO session** per user, tracked by Keycloak's own
cookie on the Keycloak origin. This project has **two clients** in the `woodgrove`
realm — `web-bff` (React app's BFF) and `intranet` (`src/Intranet`, Razor Pages) —
so SSO is observable, not just theoretical:

1. Log into the React app (`web-bff` client). Keycloak now has an SSO session.
2. Open the Intranet (`http://localhost:5262`) and click "Log in".
3. Browser is redirected to Keycloak `/authorize` for the `intranet` client.
4. Keycloak sees its own session cookie → issues a fresh auth code
   **without showing the login form**.
5. The Intranet completes the code exchange and issues its own cookie
   (`Woodgrove.Intranet`).

One login, two apps, two independent app cookies. The shared state lives only
at Keycloak. Both app sessions carry the same **`sid` claim** — the Keycloak
session id — which is what ties them together for logout (below).

### Single Logout: the problem

Each app has its own HttpOnly cookie. When the user logs out of app A, Keycloak
kills the SSO session — but app B's cookie is still valid until it expires.
Something must tell app B. Two mechanisms exist:

| Mechanism | How | Trade-off |
|---|---|---|
| **Front-channel** | Keycloak renders hidden iframes hitting each app's logout URL in the browser | Simple, but breaks with third-party-cookie blocking — fading out |
| **Back-channel** (used here) | Keycloak POSTs a signed `logout_token` JWT server-to-server to each client | Robust, works without the browser — production standard |

### Back-Channel Logout: how it works here

```
User clicks logout in app A
  │
  ▼
App A: clears own cookie, redirects to Keycloak end-session (id_token_hint)
  │
  ▼
Keycloak: ends SSO session, then for EVERY client in that session with a
registered backchannel.logout.url:
  │
  ├── POST logout_token ──▶ web-bff   http://host.docker.internal:5242/bff/backchannel-logout
  └── POST logout_token ──▶ intranet  http://host.docker.internal:5262/auth/backchannel-logout
                              │
                              ▼
                    App B validates the logout_token, then adds its sid to a
                    denylist (src/AuthShared/SessionDenylist.cs)
                              │
                              ▼
                    App B's NEXT request with the old cookie:
                    OnValidatePrincipal sees the denylisted sid
                    → RejectPrincipal() → user is anonymous
```

Revocation is **lazy** — app B's session dies on its *next* request, not at the
instant of logout. That's inherent to cookie auth: you can't reach into a
browser and delete a cookie server-side; you can only refuse to honor it.

In the Aspire dashboard, you'll see the log line `Back-channel logout: revoked Keycloak session <sid>` in the receiving app when its next request arrives.

### The logout token (anatomy)

A `logout_token` is a signed JWT (NOT an ID token). Example payload:

```json
{
  "iss": "http://localhost:8080/realms/woodgrove",
  "aud": "intranet",
  "iat": 1751600000,
  "exp": 1751600120,
  "sub": "b1c9...",
  "sid": "f6a2c9d0-...",
  "events": { "http://schemas.openid.net/event/backchannel-logout": {} },
  "jti": "..."
}
```

Validation rules (`src/AuthShared/LogoutTokenValidator.cs`):
- Signature against the realm's JWKS, plus `iss`, `aud`, lifetime — like any JWT.
- `events` **must** contain the `backchannel-logout` event URI (proves intent).
- `sid` **must** be present (we registered `backchannel.logout.session.required`).
- `nonce` **must NOT** be present (spec rule — prevents confusing a logout token
  with an ID token).

### Where the pieces live

| Piece | File |
|---|---|
| Logout-token validation | `src/AuthShared/LogoutTokenValidator.cs` |
| sid denylist | `src/AuthShared/SessionDenylist.cs` |
| Receiving endpoint | `src/AuthShared/BackchannelLogoutEndpoint.cs` (mapped at `/bff/backchannel-logout` and `/auth/backchannel-logout`) |
| Cookie rejection | `src/AuthShared/DenylistCookieEvents.cs` |
| Client registration | `keycloak/woodgrove-realm.json` → `attributes.backchannel.logout.url` |

### RP-initiated logout (the trigger)

`/bff/logout` (React app) and `/auth/logout` (Intranet) both sign out of the cookie
scheme **and** the OIDC scheme. The OIDC sign-out redirects to Keycloak's
**end-session endpoint** with an `id_token_hint` (why both apps keep
`SaveTokens = true` — the stored `id_token` proves to Keycloak *which* session to
end, silently, without a confirmation page).

---

## 5. BFF Pattern

### Why Tokens Stay Server-Side

The Browser-hosted Frontend talks only to the BFF, which holds the tokens:

```
Browser                BFF                    Keycloak
  |  -- GET /api/me --> |  -- Bearer token --> |
  |  <-- data --------- |  <-- validation ---- |
  |                     |
  | cookie only         | tokens in server memory / cookie properties
```

**Security trade-off comparison:**

| Approach | Token Storage | XSS Risk | CSRF Risk | Complexity |
|----------|--------------|-----------|-----------|------------|
| **BFF (this project)** | Server-side (encrypted in an HttpOnly cookie; readable only server-side, never by JavaScript) | None — JS cannot read HttpOnly cookie | Low (SameSite=Lax) | Higher (BFF needed) |
| Browser localStorage | Client-side | HIGH — any script can steal tokens | N/A | Simple |
| Browser sessionStorage | Client-side | HIGH — any script can steal tokens | N/A | Simple |
| In-memory (SPA) | Client-side RAM | Moderate — reset on refresh | N/A | Medium |

**Cookie settings in `src/WebBff/Program.cs`:**
```csharp
options.Cookie.Name = "Woodgrove.Bff";
options.Cookie.HttpOnly = true;       // JS cannot read it
options.Cookie.SameSite = SameSiteMode.Lax;  // CSRF protection
options.ExpireTimeSpan = TimeSpan.FromHours(8);
options.SlidingExpiration = true;
```

### YARP Token-Attach Proxy

YARP is configured in `src/WebBff/Program.cs` to forward requests from `/api/{**catch-all}` to the API, attaching the access token from the user's session:

```csharp
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddTransforms(context =>
    {
        context.AddRequestTransform(async transform =>
        {
            var token = await transform.HttpContext.GetTokenAsync("access_token");
            if (!string.IsNullOrEmpty(token))
                transform.ProxyRequest.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
        });
    });
```

The React app's `apiGet` function uses `credentials: "include"` so the browser sends the cookie automatically:
```typescript
// src/web/src/api/client.ts
export async function apiGet<T>(path: string): Promise<T> {
  const res = await fetch(path, { credentials: "include" });
  ...
}
```

### Silent Token Refresh

`src/WebBff/TokenRefresher.cs` runs on every authenticated request via `OnValidatePrincipal`. If the access token expires in fewer than 1 minute, it calls the Keycloak token endpoint with `grant_type=refresh_token` and updates the cookie. If refresh fails, `RejectPrincipal()` forces re-login.

### Deep-Link Return (`returnUrl`)

When the user clicks **Log in**, the SPA records where they were so they land back on that page after authenticating. This is a **different concern** from the *origin* fix in §7: forwarded headers decide *which origin* you return to (SPA vs BFF); `returnUrl` decides *which page/route* within the SPA.

```typescript
// src/web/src/auth/AuthContext.tsx — relative URL preserves path + query + hash
const currentReturnUrl = () =>
  window.location.pathname + window.location.search + window.location.hash;
const login  = () => (window.location.href = `/bff/login?returnUrl=${encodeURIComponent(currentReturnUrl())}`);
const logout = () => (window.location.href = `/bff/logout?returnUrl=${encodeURIComponent(currentReturnUrl())}`);
```

The BFF uses `returnUrl` as the post-login/logout `RedirectUri`, but **only after validating it is a same-site relative URL** — otherwise `/bff/logout?returnUrl=https://evil.com` would be an **open redirect** (attacker-controlled redirect after a trusted login):

```csharp
// src/WebBff/Program.cs — accept only "/path" (not "//" or "/\") or "~/path"; else "/"
static string SafeReturnUrl(string? url) =>
    !string.IsNullOrEmpty(url)
    && ((url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\')))
        || (url.Length > 1 && url[0] == '~' && url[1] == '/'))
        ? url : "/";

app.MapGet("/bff/login", (string? returnUrl) =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }));
```

> This demo's SPA has a single route, so `returnUrl` is always `/` in practice. The pattern is kept because return-to-page (plus its open-redirect validation) is standard in real apps and becomes essential the moment you add client-side routing.

---

## 6. Keycloak Concepts

### Realm

A realm is an isolated tenant in Keycloak. All users, clients, and roles are scoped to a realm. This project uses the `woodgrove` realm, auto-imported from `keycloak/woodgrove-realm.json` via Aspire's `WithRealmImport("../../keycloak")`.

**Admin console (with Aspire):** this setup runs Keycloak over **HTTPS on an Aspire-assigned (dynamic) port** — **not** `http://localhost:8080`. Open the **`keycloak` resource in the Aspire dashboard** and click its endpoint link. Log in as **`admin` / `admin`** — the admin password is pinned in `src/AppHost/Program.cs` (`AddParameter("kc-admin-password", "admin")`). Then select realm `woodgrove`.

> The port is dynamic because the Keycloak Aspire integration upgrades the endpoint to HTTPS (dev cert) in run mode and assigns the host port itself; this preview package exposes no public way to pin it. The **Aspire dashboard URL is stable**, so use it as the entry point.

> **Base-URL caveat:** the `http://localhost:8080/...` URLs used throughout this cheatsheet (issuer, discovery, token endpoint) are the **nominal** Keycloak base. With this Aspire configuration the real browser-facing base is an HTTPS URL on an Aspire-assigned port (from the dashboard); the BFF/API reach Keycloak internally via Aspire service discovery, so login still works even though the port isn't 8080. A plain `docker run` Keycloak would use `http://localhost:8080` with `admin`/`admin`.

### Client Types

| Type | `publicClient` | Has secret? | Typical use |
|------|---------------|-------------|-------------|
| **Confidential** | `false` | Yes | BFF/server-side apps — can keep a secret |
| **Public** | `true` | No | Native apps, SPAs with no back-end |
| **Bearer-only** | — | No | Pure API resources; no login flow |

In this project, `web-bff` is **confidential** (`"publicClient": false`, secret `dev-bff-secret`).

### Client Scopes

Scopes control which claims are included in tokens. The BFF requests:
```
openid  profile  email  roles  offline_access
```
- `openid` — triggers OIDC, issues an ID token.
- `profile` / `email` — standard user info claims.
- `roles` — enables the realm-role protocol mapper (see below).
- `offline_access` — issues a refresh token.

### Realm Roles

Defined in `woodgrove-realm.json`:
```json
"roles": {
  "realm": [
    { "name": "admin", "description": "Administrator" },
    { "name": "user",  "description": "Standard user" }
  ]
}
```

Assignments:
- `alice` → `["admin", "user"]`
- `bob` → `["user"]`

### Protocol Mappers

Protocol mappers inject custom data into tokens. The `web-bff` client has two:

**1. `audience-woodgrove-api`** — adds `woodgrove-api` to the `aud` claim in access tokens:
```json
{
  "name": "audience-woodgrove-api",
  "protocolMapper": "oidc-audience-mapper",
  "config": {
    "included.custom.audience": "woodgrove-api",
    "access.token.claim": "true",
    "id.token.claim": "false"
  }
}
```
Without this mapper the API's `options.Audience = "woodgrove-api"` check would reject every token with `401`.

**2. `realm-roles-flat`** — emits realm roles as a flat multivalued `roles` claim in **both** ID and access tokens:
```json
{
  "name": "realm-roles-flat",
  "protocolMapper": "oidc-usermodel-realm-role-mapper",
  "config": {
    "claim.name": "roles",
    "jsonType.label": "String",
    "multivalued": "true",
    "id.token.claim": "true",
    "access.token.claim": "true",
    "userinfo.token.claim": "true"
  }
}
```
Without this, roles would be nested under `realm_access.roles` (Keycloak default). The flat `roles` claim is what the .NET `RoleClaimType = "roles"` setting expects.

---

## 7. Aspire Concepts Used

### AppHost (`src/AppHost/Program.cs`)

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// 1. Keycloak container — port 8080, persistent volume, realm import
var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume()
    .WithRealmImport("../../keycloak");   // imports woodgrove-realm.json

// 2. API project — needs Keycloak for JWT validation
var api = builder.AddProject<Projects.Api>("api")
    .WithReference(keycloak)   // injects Keycloak URL as env var
    .WaitFor(keycloak);        // health-checks Keycloak before starting API

// 3. BFF project — needs Keycloak (OIDC) and API (to proxy to)
var bff = builder.AddProject<Projects.WebBff>("webbff")
    .WithReference(keycloak)
    .WithReference(api)
    .WaitFor(keycloak)
    .WaitFor(api);

// 4. React Vite dev server — npm app
builder.AddNpmApp("web", "../web", "dev")
    .WithReference(bff)
    .WaitFor(bff)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
```

### Key Aspire Concepts

| Concept | What it does | Example in this project |
|---------|-------------|------------------------|
| `AddKeycloak` | Adds a Keycloak Docker container as a named resource | `builder.AddKeycloak("keycloak", 8080)` |
| `WithDataVolume` | Mounts a Docker volume for Keycloak data persistence | `.WithDataVolume()` |
| `WithRealmImport` | Copies realm JSON into the container at startup | `.WithRealmImport("../../keycloak")` |
| `WithReference(resource)` | Injects service URL + connection string as env vars | `api.WithReference(keycloak)` → sets `Keycloak__*` env vars |
| `WaitFor(resource)` | Blocks startup until the dependency is healthy | `.WaitFor(keycloak)` |
| `AddNpmApp` | Registers an npm-based app (Vite dev server) | `builder.AddNpmApp("web", "../web", "dev")` |
| `WithHttpEndpoint(env: "PORT")` | Tells Vite which port to listen on via env var | `.WithHttpEndpoint(env: "PORT")` |
| `AddServiceDefaults` | Wires up OpenTelemetry, health checks, service discovery | `builder.AddServiceDefaults()` in each project |

**Service discovery:** `WithReference(keycloak)` injects the Keycloak URL. The `AddKeycloakOpenIdConnect("keycloak", ...)` / `AddKeycloakJwtBearer("keycloak", ...)` call uses the service name `"keycloak"` to resolve the URL via Aspire's service discovery, which maps to the environment variable injected by `WithReference`.

**Vite proxy resolution:** Aspire injects `services__webbff__https__0` (or `http__0`) into the Vite process. `vite.config.ts` reads this:
```typescript
const bffUrl =
  process.env["services__webbff__https__0"] ??
  process.env["services__webbff__http__0"] ??
  "http://localhost:5100";
```

The Vite dev server proxies the backend path prefixes **and the OIDC callback paths** to the BFF, so all backend requests traverse the BFF:
```ts
// vite.config.ts (server.proxy)
proxy: {
  // xfwd forwards X-Forwarded-Host/Proto so the BFF builds its OIDC
  // redirect_uri on the SPA origin (see "Two Origins in Dev" below).
  "/bff":                   { target: bffUrl, changeOrigin: true, secure: false, xfwd: true },
  "/api":                   { target: bffUrl, changeOrigin: true, secure: false, xfwd: true },
  "/signin-oidc":           { target: bffUrl, changeOrigin: true, secure: false, xfwd: true },
  "/signout-callback-oidc": { target: bffUrl, changeOrigin: true, secure: false, xfwd: true },
}
```

In development, the browser only talks to the Vite origin; all requests are proxied transparently to the BFF.

#### Two Origins in Dev — Forwarded Headers & Callback Proxying

In development there are **two origins**: the Vite dev server (e.g. `localhost:5173`, serving the SPA) and the BFF (e.g. `localhost:7228`). The full-page OIDC redirect flow breaks in two ways unless configured:

**Problem 1 — `redirect_uri` built on the wrong origin.**
`changeOrigin: true` rewrites the outgoing `Host` header to the BFF. So when the browser hits `/bff/login`, the BFF builds its OIDC `redirect_uri` on **its own** origin. Keycloak then returns the browser to the BFF, and the post-login redirect lands on `localhost:7228` instead of the SPA on `localhost:5173`.

*Fix:* Vite sends `xfwd: true` → adds `X-Forwarded-Host` (`localhost:5173`) and `X-Forwarded-Proto` (`http`). The BFF honors them (Development only) so `redirect_uri` — and the relative post-login redirect — resolve back to the SPA origin. The browser stays on the SPA the entire flow.

```csharp
// src/WebBff/Program.cs — dev only
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();   // trust the Vite dev proxy (dev only)
    o.KnownProxies.Clear();
});
// ...
if (app.Environment.IsDevelopment())
    app.UseForwardedHeaders();  // must run before auth reads scheme/host
```

**Problem 2 — the callback hits Vite's SPA fallback.**
Keycloak redirects the browser to `/signin-oidc?code=...`. If Vite doesn't proxy that path, its SPA fallback serves `index.html` and the code exchange never reaches the BFF.

*Fix:* proxy `/signin-oidc` and `/signout-callback-oidc` (the OIDC handler's default `CallbackPath` / `SignedOutCallbackPath`) to the BFF, as shown in the proxy block above.

**The fixed dev flow** — the browser never leaves the SPA origin (`localhost:5173`):

```mermaid
sequenceDiagram
    participant B as Browser
    participant V as Vite dev server<br/>(:5173, SPA origin)
    participant BFF as BFF<br/>(:7228)
    participant KC as Keycloak<br/>(:8080)

    Note over B,V: Browser only ever addresses the SPA origin (:5173)
    B->>V: GET /bff/login?returnUrl=/
    V->>BFF: proxy + xfwd:<br/>X-Forwarded-Host: localhost:5173<br/>X-Forwarded-Proto: http
    Note over BFF: UseForwardedHeaders() → host = :5173<br/>redirect_uri = http://localhost:5173/signin-oidc
    BFF-->>B: 302 → Keycloak /authorize<br/>?redirect_uri=http://localhost:5173/signin-oidc
    B->>KC: GET /authorize → login form
    B->>KC: POST alice / password
    KC-->>B: 302 → http://localhost:5173/signin-oidc?code=...
    Note over B,V: callback targets :5173 (not the BFF) → Vite proxies it
    B->>V: GET /signin-oidc?code=...
    V->>BFF: proxy /signin-oidc (xfwd)
    BFF->>KC: back-channel: exchange code + PKCE for tokens
    KC-->>BFF: id / access / refresh tokens
    BFF-->>B: 302 → / (RedirectUri on forwarded host)<br/>Set-Cookie: Woodgrove.Bff (HttpOnly)
    Note over B: Lands back on localhost:5173, authenticated ✓
```

Contrast this with the **broken** flow: without `xfwd`, the BFF would set `redirect_uri=http://localhost:7228/signin-oidc`, so Keycloak returns the browser to `:7228` and the final `302 → /` stays on the BFF.

> **Production note:** none of this is needed in production, where the BFF is the single origin that serves the SPA and receives the callback directly. Forwarded headers are gated behind `IsDevelopment()` because trusting arbitrary proxy headers on a public origin is a spoofing risk.

---

## 8. Key .NET Authentication APIs

### Cookie + OIDC Setup (`src/WebBff/Program.cs`)

```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme; // "Cookies"
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme; // "OpenIdConnect"
})
.AddCookie(options =>
{
    options.Cookie.Name = "Woodgrove.Bff";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events = new CookieAuthenticationEvents
    {
        OnValidatePrincipal = TokenRefresher.ValidateAsync  // silent refresh hook
    };
})
.AddKeycloakOpenIdConnect("keycloak", realm: "woodgrove",
    OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.ClientId = builder.Configuration["Keycloak:ClientId"];  // "web-bff"
    options.ClientSecret = builder.Configuration["Keycloak:ClientSecret"];  // "dev-bff-secret"
    options.ResponseType = OpenIdConnectResponseType.Code;  // Auth Code
    options.UsePkce = true;                                 // + PKCE
    options.RequireHttpsMetadata = false;                   // DEV ONLY
    options.SaveTokens = true;                              // store tokens in cookie properties
    options.GetClaimsFromUserInfoEndpoint = true;
    options.Scope.Add("roles");
    options.Scope.Add("offline_access");
    options.TokenValidationParameters.NameClaimType = "preferred_username";
    options.TokenValidationParameters.RoleClaimType = "roles";
});
```

### JWT Bearer Setup (`src/Api/Program.cs`)

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddKeycloakJwtBearer("keycloak", realm: "woodgrove", options =>
    {
        options.Audience = "woodgrove-api";           // must match aud claim
        options.RequireHttpsMetadata = false;          // DEV ONLY
        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.TokenValidationParameters.RoleClaimType = "roles";  // from RolesClaimsHelper
    });
```

`AddKeycloakJwtBearer` auto-configures the authority to `http://<keycloak>/realms/woodgrove` and fetches JWKS automatically.

### Authorization Policies & Endpoint Protection

```csharp
// Require authenticated user
app.MapGet("/api/me", ...).RequireAuthorization();

// Require specific role
app.MapGet("/api/admin", ...).RequireAuthorization(p => p.RequireRole("admin"));

// Attribute-based (equivalent)
[Authorize(Roles = "admin")]
```

### `OnValidatePrincipal` Refresh Hook

Called on every authenticated request by the cookie middleware. Source: `src/WebBff/TokenRefresher.cs`.

Flow:
1. Read `expires_at` from cookie properties.
2. If expiry is more than 1 minute away → do nothing.
3. Call Keycloak token endpoint with `grant_type=refresh_token`.
4. On success → update `access_token`, `refresh_token`, `expires_at` in cookie properties; set `ShouldRenew = true` to re-issue the encrypted cookie.
5. On any failure → `RejectPrincipal()` → forces a new login redirect.

---

## 9. Common Pitfalls

### Audience Mismatch
**Symptom:** API returns `401 Unauthorized` with error `invalid_token` / `audience validation failed`.

**Cause:** The `aud` claim in the access token does not contain `woodgrove-api`.

**Fix:** Ensure the `audience-woodgrove-api` protocol mapper is present in the `web-bff` client config in Keycloak. Check `woodgrove-realm.json` — the mapper must have `"included.custom.audience": "woodgrove-api"` and `"access.token.claim": "true"`.

---

### Redirect URI Not Registered
**Symptom:** Keycloak shows `Invalid parameter: redirect_uri` after login attempt.

**Cause:** The callback URL the BFF sends does not match any registered `redirectUris` for the client.

**Fix:** In `woodgrove-realm.json`, `"redirectUris": ["*"]` is a dev-only wildcard. In production, list the exact callback URI (e.g., `https://app.example.com/signin-oidc`).

---

### HTTPS Metadata in Dev
**Symptom:** `IOException: The remote certificate is invalid` or `HttpRequestException` during OIDC metadata fetch.

**Cause:** `RequireHttpsMetadata` defaults to `true` but Keycloak in dev uses HTTP.

**Fix:** Both BFF and API set `options.RequireHttpsMetadata = false`. This is a **dev-only** setting — never disable in production.

---

### Clock Skew
**Symptom:** Tokens that should be valid are rejected with `Lifetime validation failed. The token is not yet valid`.

**Cause:** The server clock is out of sync with Keycloak. JWT validation uses wall-clock time.

**Fix:** Ensure NTP is running on all machines. .NET's `TokenValidationParameters.ClockSkew` defaults to 5 minutes tolerance, which usually covers small drift. Increase it temporarily for debugging: `ClockSkew = TimeSpan.FromMinutes(10)`.

---

### Keycloak Admin Console Is Blank / Not on `localhost:8080`
**Symptom:** Browsing to `http://localhost:8080` shows a white/blank page (or "can't connect"). It can *look* like a session was reused, but it isn't.

**Cause:** With this Aspire configuration Keycloak is served over **HTTPS on an Aspire-assigned (dynamic) port** (e.g. `https://localhost:58074`), and **nothing listens on `8080`** — the browser just fails to connect.

**Fix:** Open the **`keycloak` resource in the Aspire dashboard** and click its endpoint link (the dashboard URL is stable even though Keycloak's port is not). Log in as **`admin` / `admin`** (pinned in `AppHost`). Accept the dev-cert warning.

**Note on SSO (why it is *not* credential reuse):** Keycloak SSO session cookies are **per-realm** (path-scoped to `/realms/<realm>/`). A user logged into the `woodgrove` realm has no session in the `master` realm that backs the admin console, so a `woodgrove` login can never silently sign you into the admin console.

---

### Post-Login Lands on the BFF, Not the SPA (Dev)
**Symptom:** After a successful Keycloak login the browser ends up on the BFF origin (e.g. `localhost:7228`) instead of the SPA (`localhost:5173`).

**Cause:** With `changeOrigin: true`, the Vite proxy rewrites the `Host` header, so the BFF builds its OIDC `redirect_uri` on its own origin and Keycloak sends the browser there.

**Fix:** Forward the original host/proto from Vite (`xfwd: true`) and honor it in the BFF via `UseForwardedHeaders()` (Development only); also proxy `/signin-oidc` and `/signout-callback-oidc`. See §7 "Two Origins in Dev".

---

### Role-Claim Type Mismatch
**Symptom:** `[Authorize(Roles = "admin")]` or `.RequireRole("admin")` returns `403` even though the token contains `"roles": ["admin"]`.

**Cause:** .NET maps role checks to the claim type specified in `RoleClaimType`. If it is not set to `"roles"`, the framework looks for `ClaimTypes.Role` (`http://schemas.microsoft.com/ws/2008/06/identity/claims/role`) instead.

**Fix:** Both BFF and API set `options.TokenValidationParameters.RoleClaimType = "roles"` (matching the flat `roles` claim emitted by the `realm-roles-flat` mapper). Also defined in `src/Api/RolesClaimsHelper.cs` as a constant.

---

### `dev-bff-secret` Is Dev-Only
**Symptom:** Client secret exposed in source control or logs.

**Cause:** `"secret": "dev-bff-secret"` is hardcoded in `woodgrove-realm.json` and in dev configuration.

**Fix:** In production, generate a strong random secret, store it in a secrets manager (Azure Key Vault, AWS Secrets Manager, etc.), and inject via environment variable. Never commit real secrets to source control.

---

### Back-Channel Logout URL Not Reachable From the Container

**Symptom:** logout in one app doesn't log out the other; no "revoked Keycloak
session" line in the other app's logs; Keycloak logs show a failed POST.
Keycloak runs **inside Docker** — `localhost` there is the container itself. The
realm registers `http://host.docker.internal:<port>/...` so the container can
reach apps on the host, which requires the pinned ports (5242 BFF, 5262 Intranet)
to actually match `launchSettings.json`.

---

### Logout Token Issuer Mismatch

**Symptom:** the receiving app logs `Rejected logout token: ... issuer`.
The `iss` Keycloak writes into logout tokens must equal the issuer the app saw in
the discovery document. If Keycloak's hostname settings produce a different
URL for backend-initiated tokens than for browser-facing discovery, validation
fails closed. Fix by pinning the container's hostname (e.g. `KC_HOSTNAME`) so
both views agree.

---

### Stale Cookie Without a `sid` Claim

**Symptom:** a session created *before* back-channel logout was added never gets
revoked. The denylist keys on the `sid` claim; a cookie principal without one is
skipped (`DenylistCookieEvents`). Fix: log out/in once to mint a fresh session.
In-memory denylist is also single-instance and empties on restart — dev-only
simplification; production uses a distributed cache.

---

## 10. Handy Snippets

### Discovery Document URL

Keycloak's OIDC discovery endpoint for this project:

```
http://localhost:8080/realms/woodgrove/.well-known/openid-configuration
```

Fetch it to see all endpoints (token, authorize, JWKS, end-session):
```bash
curl http://localhost:8080/realms/woodgrove/.well-known/openid-configuration | jq .
```

Key fields returned:
- `issuer` — `http://localhost:8080/realms/woodgrove`
- `authorization_endpoint` — where the browser goes to log in
- `token_endpoint` — where the BFF exchanges codes for tokens
- `jwks_uri` — public keys for verifying JWTs
- `end_session_endpoint` — single logout

---

### curl Token Request (Password Grant — Dev/Test Only)

> **Note:** The `web-bff` client has `directAccessGrantsEnabled: false`. To use password grant for quick API testing, either enable it temporarily in the Keycloak Admin UI or create a separate dev-test client with direct grants enabled.

```bash
# Get an access token using Resource Owner Password Credentials (ROPC)
TOKEN=$(curl -s \
  -X POST "http://localhost:8080/realms/woodgrove/protocol/openid-connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password" \
  -d "client_id=web-bff" \
  -d "client_secret=dev-bff-secret" \
  -d "username=alice" \
  -d "password=password" \
  -d "scope=openid roles" \
  | jq -r '.access_token')

echo "Token: $TOKEN"

# Use the token against the API directly (bypassing BFF)
curl -H "Authorization: Bearer $TOKEN" http://localhost:<api-port>/api/me
curl -H "Authorization: Bearer $TOKEN" http://localhost:<api-port>/api/admin
```

Replace `<api-port>` with the port shown in the Aspire dashboard (typically shown as the `api` resource endpoint).

---

### How to Decode a JWT

**Option 1 — Browser (safest for real tokens):**
Open [https://jwt.io](https://jwt.io) and paste the token. Never paste production tokens into online tools.

**Option 2 — Dev endpoint (this project):**
```bash
# Shows decoded id_token and access_token (dev environment only)
curl -s http://localhost:<bff-port>/bff/debug/tokens \
  --cookie "Woodgrove.Bff=<your-cookie-value>" | jq .
```

**Option 3 — bash one-liner (no external tools):**
```bash
# Decode the payload section of a JWT
TOKEN="<paste-token-here>"
echo $TOKEN | cut -d. -f2 | base64 --decode 2>/dev/null | jq .
# (Some base64 implementations need padding — add == if it fails)
echo $TOKEN | cut -d. -f2 | awk '{ n=length($0)%4; if(n>0){for(i=0;i<4-n;i++) $0=$0"="}; print }' | base64 --decode | jq .
```

**Option 4 — PowerShell:**
```powershell
$token = "<paste-token-here>"
$payload = $token.Split('.')[1]
# Add padding
$pad = $payload.Length % 4
if ($pad -gt 0) { $payload += "=" * (4 - $pad) }
$bytes = [Convert]::FromBase64String($payload.Replace('-','+').Replace('_','/'))
[System.Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
```

---

## Quick Reference Card

```
Realm:        woodgrove
Client:       web-bff  (confidential, secret: dev-bff-secret — DEV ONLY)
API audience: woodgrove-api
PKCE:         enabled (UsePkce = true)
Roles:        admin, user
Users:        alice (admin+user) | bob (user) — password: password

Cookie:       Woodgrove.Bff  (HttpOnly, SameSite=Lax, 8h)
BFF scopes:   openid profile email roles offline_access
Token TTL:    300 s (5 min) access | refresh at <1 min remaining

Discovery:    <keycloak-url>/realms/woodgrove/.well-known/openid-configuration
Admin UI:     <keycloak-url>/admin/   (user: admin / password: admin — pinned in AppHost)
              ^ <keycloak-url> = HTTPS on an Aspire-assigned (dynamic) port. Open it via
                the `keycloak` link in the Aspire dashboard (whose own URL is stable).
                Nominal value in this doc is http://localhost:8080.

BFF endpoints:
  GET /bff/login?returnUrl=  → triggers OIDC challenge (returnUrl validated same-site)
  GET /bff/logout?returnUrl= → deletes cookie + end-session at Keycloak
  GET /bff/user           → returns name + roles (or 401)
  GET /bff/debug/tokens   → decoded tokens (dev only)

API endpoints:
  GET /api/public         → 200 (anonymous)
  GET /api/me             → 200 (any authenticated) | 401
  GET /api/admin          → 200 (role: admin) | 403

Source files:
  src/AppHost/Program.cs            — Aspire orchestration
  src/WebBff/Program.cs             — BFF auth + YARP config
  src/WebBff/TokenRefresher.cs      — silent refresh hook
  src/Api/Program.cs                — JWT bearer + endpoints
  src/Api/RolesClaimsHelper.cs      — claim type constants
  src/web/vite.config.ts            — Vite proxy + port config
  src/web/src/api/client.ts         — apiGet (credentials: include)
  keycloak/woodgrove-realm.json     — realm, client, users, mappers
```
