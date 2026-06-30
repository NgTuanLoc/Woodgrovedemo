# Keycloak + .NET + Aspire BFF Auth Demo — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a runnable learning project demonstrating OAuth2 / OIDC / SSO using Keycloak, a React 19 SPA, a .NET BFF (cookie + OIDC + token custody), and a JWT-protected .NET API, all orchestrated by .NET Aspire.

**Architecture:** Aspire AppHost starts Keycloak (realm auto-imported), the API, the BFF, and the React dev server. The browser only ever talks to the BFF/Vite origin via an HttpOnly cookie; the BFF does Authorization Code + PKCE login, custodies tokens, refreshes them, and reverse-proxies `/api/*` to the API with a bearer token attached.

**Tech Stack:** .NET 11 (preview 5), Aspire (latest), `Aspire.Hosting.Keycloak`, `Aspire.Keycloak.Authentication`, YARP, React 19 + Vite + TypeScript, Vitest, Keycloak (latest container image), Docker.

## Global Constraints

- Target framework: `net11.0` for all .NET projects.
- Realm name: `woodgrove`. API audience: `woodgrove-api`. BFF client id: `web-bff`. Dev client secret: `dev-bff-secret` (DEV ONLY — documented as a pitfall).
- Realm roles: `admin`, `user`. Roles surfaced as a flat multivalued `roles` claim in both ID and access tokens.
- Test users: `alice` (roles `admin`,`user`, password `password`), `bob` (role `user`, password `password`).
- Tokens MUST NOT be exposed to browser JS for API calls — the BFF holds them; the browser uses a cookie.
- Dev only: `RequireHttpsMetadata = false`, `redirectUris`/`webOrigins` = `*`. These are flagged as production pitfalls in the cheatsheet.
- Entry point: `dotnet run` from `src/AppHost`.
- Commit after every task. Conventional commit messages.

---

## File Structure

```
Woodgrovedemo.sln
src/
  AppHost/
    AppHost.csproj
    Program.cs
    Properties/launchSettings.json
  ServiceDefaults/
    ServiceDefaults.csproj
    Extensions.cs
  Api/
    Api.csproj
    Program.cs
    RolesClaimsHelper.cs
    appsettings.json
  WebBff/
    WebBff.csproj
    Program.cs
    TokenRefresher.cs
    appsettings.json
  web/
    package.json
    vite.config.ts
    tsconfig.json
    index.html
    src/
      main.tsx
      App.tsx
      auth/AuthContext.tsx
      auth/useAuth.ts
      components/Profile.tsx
      components/AdminSection.tsx
      components/TokenPanel.tsx
      api/client.ts
    src/auth/AuthContext.test.tsx
keycloak/
  woodgrove-realm.json
tests/
  Api.Tests/
    Api.Tests.csproj
    PublicEndpointTests.cs
    ProtectedEndpointTests.cs
docs/
  cheatsheet.md
  superpowers/specs/2026-06-30-keycloak-dotnet-aspire-bff-auth-design.md
  superpowers/plans/2026-06-30-keycloak-dotnet-aspire-bff-auth.md
```

---

### Task 1: Solution + Aspire scaffolding (empty dashboard runs)

**Files:**
- Create: `Woodgrovedemo.sln`, `src/AppHost/AppHost.csproj`, `src/AppHost/Program.cs`, `src/ServiceDefaults/ServiceDefaults.csproj`, `src/ServiceDefaults/Extensions.cs`

**Interfaces:**
- Produces: `ServiceDefaults` extension methods `AddServiceDefaults()` and `MapDefaultEndpoints()` on `IHostApplicationBuilder` / `WebApplication`, consumed by Api and WebBff.

- [ ] **Step 1: Install/verify Aspire templates**

Run:
```bash
dotnet new install Aspire.ProjectTemplates
dotnet new list aspire
```
Expected: lists `aspire-apphost`, `aspire-servicedefaults`, etc.

- [ ] **Step 2: Create solution and Aspire projects from templates**

Run:
```bash
cd "D:/Study/AspNetCore/Woodgrovedemo"
dotnet new sln -n Woodgrovedemo
dotnet new aspire-apphost -n AppHost -o src/AppHost -f net11.0
dotnet new aspire-servicedefaults -n ServiceDefaults -o src/ServiceDefaults -f net11.0
dotnet sln add src/AppHost/AppHost.csproj src/ServiceDefaults/ServiceDefaults.csproj
```
Expected: projects created, added to solution.

- [ ] **Step 3: Add the Keycloak hosting package to AppHost**

Run:
```bash
dotnet add src/AppHost/AppHost.csproj package Aspire.Hosting.Keycloak
dotnet add src/AppHost/AppHost.csproj package Aspire.Hosting.NodeJs
```
Expected: packages restored.

- [ ] **Step 4: Write minimal AppHost Program.cs (no resources yet)**

`src/AppHost/Program.cs`:
```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Resources are added in later tasks.

builder.Build().Run();
```

- [ ] **Step 5: Run to verify the dashboard launches**

Run: `dotnet run --project src/AppHost`
Expected: Aspire dashboard URL printed; dashboard opens with an empty resource list. Stop with Ctrl+C.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffold Aspire AppHost and ServiceDefaults"
```

---

### Task 2: Keycloak resource + realm auto-import

**Files:**
- Create: `keycloak/woodgrove-realm.json`
- Modify: `src/AppHost/Program.cs`

**Interfaces:**
- Produces: Aspire resource named `keycloak` (referenced by Api and WebBff in later tasks); realm `woodgrove` with client `web-bff`, audience `woodgrove-api`, roles, and users.

- [ ] **Step 1: Write the realm import file**

`keycloak/woodgrove-realm.json`:
```json
{
  "realm": "woodgrove",
  "enabled": true,
  "sslRequired": "none",
  "accessTokenLifespan": 300,
  "roles": {
    "realm": [
      { "name": "admin", "description": "Administrator" },
      { "name": "user", "description": "Standard user" }
    ]
  },
  "clients": [
    {
      "clientId": "web-bff",
      "name": "Woodgrove BFF",
      "enabled": true,
      "protocol": "openid-connect",
      "publicClient": false,
      "secret": "dev-bff-secret",
      "standardFlowEnabled": true,
      "directAccessGrantsEnabled": false,
      "serviceAccountsEnabled": false,
      "redirectUris": ["*"],
      "webOrigins": ["*"],
      "fullScopeAllowed": true,
      "attributes": { "post.logout.redirect.uris": "*" },
      "protocolMappers": [
        {
          "name": "audience-woodgrove-api",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-audience-mapper",
          "config": {
            "included.custom.audience": "woodgrove-api",
            "access.token.claim": "true",
            "id.token.claim": "false"
          }
        },
        {
          "name": "realm-roles-flat",
          "protocol": "openid-connect",
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
      ]
    }
  ],
  "users": [
    {
      "username": "alice",
      "enabled": true,
      "emailVerified": true,
      "email": "alice@example.com",
      "firstName": "Alice",
      "lastName": "Admin",
      "credentials": [{ "type": "password", "value": "password", "temporary": false }],
      "realmRoles": ["admin", "user"]
    },
    {
      "username": "bob",
      "enabled": true,
      "emailVerified": true,
      "email": "bob@example.com",
      "firstName": "Bob",
      "lastName": "User",
      "credentials": [{ "type": "password", "value": "password", "temporary": false }],
      "realmRoles": ["user"]
    }
  ]
}
```

- [ ] **Step 2: Add the Keycloak resource to AppHost**

Replace `src/AppHost/Program.cs` with:
```csharp
var builder = DistributedApplication.CreateBuilder(args);

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume()
    .WithRealmImport("../../keycloak");

builder.Build().Run();
```

- [ ] **Step 3: Run and verify Keycloak imports the realm**

Run: `dotnet run --project src/AppHost`
Expected: Aspire dashboard shows `keycloak` resource becoming healthy. Open its endpoint, log into the Keycloak admin console (admin credentials shown in the dashboard's `keycloak` resource details/env), switch to realm `woodgrove`, and confirm users `alice`/`bob` and roles `admin`/`user` exist. Stop with Ctrl+C.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: add Keycloak resource with woodgrove realm import"
```

---

### Task 3: API project — JWT bearer + role-gated endpoints

**Files:**
- Create: `src/Api/Api.csproj`, `src/Api/Program.cs`, `src/Api/RolesClaimsHelper.cs`, `src/Api/appsettings.json`, `tests/Api.Tests/Api.Tests.csproj`, `tests/Api.Tests/PublicEndpointTests.cs`, `tests/Api.Tests/ProtectedEndpointTests.cs`
- Modify: `src/AppHost/Program.cs`, `Woodgrovedemo.sln`

**Interfaces:**
- Consumes: `keycloak` resource (Task 2); `AddServiceDefaults()` (Task 1).
- Produces: HTTP endpoints `GET /api/public` (anon), `GET /api/me` (`[Authorize]`), `GET /api/admin` (`admin` role); Aspire resource `api`.

- [ ] **Step 1: Create the API project and test project**

Run:
```bash
dotnet new web -n Api -o src/Api -f net11.0
dotnet add src/Api/Api.csproj reference src/ServiceDefaults/ServiceDefaults.csproj
dotnet add src/Api/Api.csproj package Aspire.Keycloak.Authentication
dotnet new xunit -n Api.Tests -o tests/Api.Tests -f net11.0
dotnet add tests/Api.Tests/Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/Api.Tests/Api.Tests.csproj reference src/Api/Api.csproj
dotnet sln add src/Api/Api.csproj tests/Api.Tests/Api.Tests.csproj
```

- [ ] **Step 2: Write the failing public-endpoint test**

`tests/Api.Tests/PublicEndpointTests.cs`:
```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

public class PublicEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public PublicEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Public_endpoint_returns_200_without_token()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/public");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Me_endpoint_returns_401_without_token()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/Api.Tests`
Expected: FAIL — `Program` not accessible / endpoints not defined.

- [ ] **Step 4: Write the roles claims helper**

`src/Api/RolesClaimsHelper.cs`:
```csharp
using System.Security.Claims;

public static class RolesClaimsHelper
{
    // Keycloak emits a flat multivalued "roles" claim (see realm import).
    public const string RoleClaimType = "roles";
    public const string NameClaimType = "preferred_username";
}
```

- [ ] **Step 5: Write the API Program.cs**

`src/Api/Program.cs`:
```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddKeycloakJwtBearer("keycloak", realm: "woodgrove", options =>
    {
        options.Audience = "woodgrove-api";
        options.RequireHttpsMetadata = false; // DEV ONLY
        options.TokenValidationParameters.NameClaimType = RolesClaimsHelper.NameClaimType;
        options.TokenValidationParameters.RoleClaimType = RolesClaimsHelper.RoleClaimType;
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/public", () => new { message = "public, no auth needed" });

app.MapGet("/api/me", (HttpContext ctx) =>
    new
    {
        name = ctx.User.Identity?.Name,
        claims = ctx.User.Claims.Select(c => new { c.Type, c.Value })
    })
    .RequireAuthorization();

app.MapGet("/api/admin", () => new { message = "secret admin data" })
    .RequireAuthorization(p => p.RequireRole("admin"));

app.Run();

public partial class Program { }
```

- [ ] **Step 6: Register the API in AppHost**

Update `src/AppHost/Program.cs` to add (before `builder.Build()`):
```csharp
var api = builder.AddProject<Projects.Api>("api")
    .WithReference(keycloak)
    .WaitFor(keycloak);
```
And add the project reference:
```bash
dotnet add src/AppHost/AppHost.csproj reference src/Api/Api.csproj
```

- [ ] **Step 7: Run the public/401 tests to verify they pass**

Run: `dotnet test tests/Api.Tests`
Expected: PASS (the two tests from Step 2). The `WebApplicationFactory` boots the API; the Keycloak authority is unreachable in the test host but unauthenticated requests still resolve (200 for public, 401 for `/api/me`).

- [ ] **Step 8: Write the role-based tests using a fake JWT scheme**

`tests/Api.Tests/ProtectedEndpointTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// Replaces the real Keycloak JWT validation with a test scheme driven by request headers:
//   X-Test-User: <name>    X-Test-Roles: admin,user
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o,
        ILoggerFactory l, UrlEncoder e) : base(o, l, e) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new(RolesClaimsHelper.NameClaimType, user!) };
        if (Request.Headers.TryGetValue("X-Test-Roles", out var roles))
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => new Claim(RolesClaimsHelper.RoleClaimType, r.Trim())));

        var identity = new ClaimsIdentity(claims, "Test",
            RolesClaimsHelper.NameClaimType, RolesClaimsHelper.RoleClaimType);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity),
            JwtBearerDefaults.AuthenticationScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class ProtectedEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ProtectedEndpointTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
             .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                 JwtBearerDefaults.AuthenticationScheme, _ => { })));

    private HttpClient ClientFor(string user, string roles)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-User", user);
        c.DefaultRequestHeaders.Add("X-Test-Roles", roles);
        return c;
    }

    [Fact]
    public async Task Me_returns_200_for_authenticated_user()
    {
        var res = await ClientFor("bob", "user").GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Admin_returns_403_for_non_admin()
    {
        var res = await ClientFor("bob", "user").GetAsync("/api/admin");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Admin_returns_200_for_admin()
    {
        var res = await ClientFor("alice", "admin,user").GetAsync("/api/admin");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
```

- [ ] **Step 9: Run all API tests to verify they pass**

Run: `dotnet test tests/Api.Tests`
Expected: PASS — all five tests green.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: add JWT-protected API with role-based endpoints and tests"
```

---

### Task 4: WebBff — cookie + OIDC login, BFF endpoints, YARP proxy

**Files:**
- Create: `src/WebBff/WebBff.csproj`, `src/WebBff/Program.cs`, `src/WebBff/appsettings.json`
- Modify: `src/AppHost/Program.cs`, `Woodgrovedemo.sln`

**Interfaces:**
- Consumes: `keycloak` (Task 2), `api` (Task 3), `AddServiceDefaults()` (Task 1).
- Produces: endpoints `GET /bff/login`, `GET /bff/logout`, `GET /bff/user`; reverse proxy `/api/{**catch-all}` → `api` with bearer token; Aspire resource `webbff`. Auth cookie name `Woodgrove.Bff`.

- [ ] **Step 1: Create the BFF project and add packages**

Run:
```bash
dotnet new web -n WebBff -o src/WebBff -f net11.0
dotnet add src/WebBff/WebBff.csproj reference src/ServiceDefaults/ServiceDefaults.csproj
dotnet add src/WebBff/WebBff.csproj package Aspire.Keycloak.Authentication
dotnet add src/WebBff/WebBff.csproj package Microsoft.AspNetCore.Authentication.OpenIdConnect
dotnet add src/WebBff/WebBff.csproj package Yarp.ReverseProxy
dotnet add src/WebBff/WebBff.csproj package Microsoft.Extensions.ServiceDiscovery.Yarp --prerelease
dotnet sln add src/WebBff/WebBff.csproj
```

- [ ] **Step 2: Write the YARP route config**

`src/WebBff/appsettings.json`:
```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "Keycloak": { "ClientId": "web-bff", "ClientSecret": "dev-bff-secret" },
  "ReverseProxy": {
    "Routes": {
      "api-route": {
        "ClusterId": "api-cluster",
        "Match": { "Path": "/api/{**catch-all}" },
        "AuthorizationPolicy": "authenticated"
      }
    },
    "Clusters": {
      "api-cluster": {
        "Destinations": { "api": { "Address": "http://api" } }
      }
    }
  }
}
```

- [ ] **Step 3: Write the BFF Program.cs**

`src/WebBff/Program.cs`:
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "Woodgrove.Bff";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    })
    .AddKeycloakOpenIdConnect("keycloak", realm: "woodgrove",
        OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = builder.Configuration["Keycloak:ClientId"];
        options.ClientSecret = builder.Configuration["Keycloak:ClientSecret"];
        options.ResponseType = OpenIdConnectResponseType.Code; // Auth Code + PKCE
        options.UsePkce = true;
        options.RequireHttpsMetadata = false; // DEV ONLY
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("roles");
        options.Scope.Add("offline_access"); // refresh token
        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.TokenValidationParameters.RoleClaimType = "roles";
        options.SignedOutRedirectUri = "/";
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("authenticated", p => p.RequireAuthenticatedUser());
});

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
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        });
    });

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();

// --- BFF endpoints ---
app.MapGet("/bff/login", (string? returnUrl) =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl ?? "/" }))
    .AllowAnonymous();

app.MapGet("/bff/logout", () =>
    Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
        new[] { CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme }));

app.MapGet("/bff/user", (HttpContext ctx) =>
{
    if (ctx.User.Identity?.IsAuthenticated != true)
        return Results.Unauthorized();
    return Results.Ok(new
    {
        isAuthenticated = true,
        name = ctx.User.Identity!.Name,
        roles = ctx.User.FindAll("roles").Select(c => c.Value),
        claims = ctx.User.Claims.Select(c => new { c.Type, c.Value })
    });
}).AllowAnonymous();

app.MapReverseProxy();

app.Run();
```

- [ ] **Step 4: Register the BFF in AppHost**

Update `src/AppHost/Program.cs` to add (before `builder.Build()`):
```csharp
var bff = builder.AddProject<Projects.WebBff>("webbff")
    .WithReference(keycloak)
    .WithReference(api)
    .WaitFor(keycloak)
    .WaitFor(api);
```
And add the reference:
```bash
dotnet add src/AppHost/AppHost.csproj reference src/WebBff/WebBff.csproj
```

- [ ] **Step 5: Run and manually verify the login flow**

Run: `dotnet run --project src/AppHost`
Then, from the dashboard, open the `webbff` endpoint and:
1. `GET /bff/user` → expect **401**.
2. Visit `/bff/login` → redirected to Keycloak login → sign in as `alice`/`password` → redirected back.
3. `GET /bff/user` → expect **200** with `name: "alice"` and `roles` including `admin`.
4. `GET /api/me` (through the BFF) → expect **200** with claims (token forwarded).
5. `GET /bff/logout` → cookie cleared; `GET /bff/user` → **401** again.

Stop with Ctrl+C.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add BFF with cookie+OIDC login and token-forwarding proxy"
```

---

### Task 5: BFF transparent token refresh

**Files:**
- Create: `src/WebBff/TokenRefresher.cs`
- Modify: `src/WebBff/Program.cs`

**Interfaces:**
- Consumes: cookie auth + OIDC config (Task 4).
- Produces: `CookieAuthenticationEvents.OnValidatePrincipal` wired to `TokenRefresher.ValidateAsync`, refreshing the access token before expiry using the stored refresh token.

- [ ] **Step 1: Write the token refresher**

`src/WebBff/TokenRefresher.cs`:
```csharp
using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

public static class TokenRefresher
{
    // Refresh when the access token has this much (or less) life remaining.
    private static readonly TimeSpan RefreshThreshold = TimeSpan.FromMinutes(1);

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var tokens = context.Properties.GetTokens().ToList();
        var expiresAtToken = tokens.FirstOrDefault(t => t.Name == "expires_at");
        var refreshToken = tokens.FirstOrDefault(t => t.Name == "refresh_token");
        if (expiresAtToken is null || refreshToken is null) return;

        if (!DateTimeOffset.TryParse(expiresAtToken.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var expiresAt))
            return;

        if (expiresAt - DateTimeOffset.UtcNow > RefreshThreshold) return; // still fresh

        var services = context.HttpContext.RequestServices;
        var oidcOptions = services.GetRequiredService<
            Microsoft.Extensions.Options.IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        var config = await oidcOptions.ConfigurationManager!
            .GetConfigurationAsync(context.HttpContext.RequestAborted);

        var httpClient = oidcOptions.Backchannel;
        var response = await httpClient.PostAsync(config.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = oidcOptions.ClientId!,
                ["client_secret"] = oidcOptions.ClientSecret!,
                ["refresh_token"] = refreshToken.Value
            }), context.HttpContext.RequestAborted);

        if (!response.IsSuccessStatusCode)
        {
            context.RejectPrincipal(); // forces re-login
            return;
        }

        using var payload = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
        var root = payload.RootElement;

        var newAccess = root.GetProperty("access_token").GetString()!;
        var newRefresh = root.TryGetProperty("refresh_token", out var rt)
            ? rt.GetString()! : refreshToken.Value;
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        var newExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
            .ToString("o", CultureInfo.InvariantCulture);

        context.Properties.UpdateTokenValue("access_token", newAccess);
        context.Properties.UpdateTokenValue("refresh_token", newRefresh);
        context.Properties.UpdateTokenValue("expires_at", newExpiresAt);
        if (root.TryGetProperty("id_token", out var idt))
            context.Properties.UpdateTokenValue("id_token", idt.GetString()!);

        context.ShouldRenew = true; // re-issue the cookie with updated tokens
    }
}
```

- [ ] **Step 2: Wire the event in Program.cs**

In `src/WebBff/Program.cs`, update the `.AddCookie(...)` call to add the event handler:
```csharp
    .AddCookie(options =>
    {
        options.Cookie.Name = "Woodgrove.Bff";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = TokenRefresher.ValidateAsync
        };
    })
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/WebBff`
Expected: build succeeds.

- [ ] **Step 4: Manually verify refresh**

Run: `dotnet run --project src/AppHost`. Log in as `alice`. Note `accessTokenLifespan` is 300s (realm import). Wait ~5 minutes (or temporarily lower `accessTokenLifespan` in the realm to 60), keep calling `GET /api/me` through the BFF, and confirm it keeps returning 200 (token silently refreshed). In the Keycloak admin sessions view you can see the refresh activity. Stop with Ctrl+C.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: transparent access-token refresh in the BFF cookie pipeline"
```

---

### Task 6: React app scaffolding + Aspire wiring + Vite proxy

**Files:**
- Create: `src/web/` (Vite project), `src/web/vite.config.ts`, `src/web/src/api/client.ts`
- Modify: `src/AppHost/Program.cs`

**Interfaces:**
- Consumes: `webbff` resource (Task 4).
- Produces: React dev server (Aspire resource `web`) proxying `/bff` and `/api` to the BFF; `apiGet(path)` helper that calls with `credentials: 'include'`.

- [ ] **Step 1: Scaffold the Vite React + TS app**

Run:
```bash
cd "D:/Study/AspNetCore/Woodgrovedemo/src"
npm create vite@latest web -- --template react-ts
cd web
npm install
npm install -D vitest @testing-library/react @testing-library/jest-dom jsdom
```

- [ ] **Step 2: Configure Vite proxy to the BFF (via Aspire service discovery env)**

`src/web/vite.config.ts`:
```ts
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Aspire injects the BFF URL as an env var when referenced (services__webbff__http__0).
const bffUrl =
  process.env["services__webbff__https__0"] ??
  process.env["services__webbff__http__0"] ??
  "http://localhost:5100";

export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env.PORT ?? 5173),
    proxy: {
      "/bff": { target: bffUrl, changeOrigin: true, secure: false },
      "/api": { target: bffUrl, changeOrigin: true, secure: false },
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: [],
  },
});
```

- [ ] **Step 3: Write the API client helper**

`src/web/src/api/client.ts`:
```ts
export async function apiGet<T>(path: string): Promise<T> {
  const res = await fetch(path, { credentials: "include" });
  if (!res.ok) {
    throw new Error(`Request failed: ${res.status}`);
  }
  return (await res.json()) as T;
}
```

- [ ] **Step 4: Register the React app in AppHost**

Update `src/AppHost/Program.cs` to add (before `builder.Build()`):
```csharp
builder.AddNpmApp("web", "../web", "dev")
    .WithReference(bff)
    .WaitFor(bff)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();
```

- [ ] **Step 5: Run and verify the React app loads via Aspire**

Run: `dotnet run --project src/AppHost`
Expected: dashboard shows a `web` resource; opening its endpoint shows the default Vite React page. Stop with Ctrl+C.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add React 19 Vite app wired into Aspire with BFF proxy"
```

---

### Task 7: React auth context + login/logout + claims viewer

**Files:**
- Create: `src/web/src/auth/AuthContext.tsx`, `src/web/src/auth/useAuth.ts`, `src/web/src/auth/AuthContext.test.tsx`, `src/web/src/components/Profile.tsx`
- Modify: `src/web/src/App.tsx`, `src/web/src/main.tsx`

**Interfaces:**
- Consumes: `apiGet` (Task 6); BFF `GET /bff/user`, `/bff/login`, `/bff/logout` (Task 4).
- Produces: `useAuth()` returning `{ user, loading, login(), logout() }` where `user` is `{ isAuthenticated, name, roles, claims } | null`.

- [ ] **Step 1: Write the failing auth-context test**

`src/web/src/auth/AuthContext.test.tsx`:
```tsx
import { render, screen, waitFor } from "@testing-library/react";
import { AuthProvider } from "./AuthContext";
import { useAuth } from "./useAuth";

function Probe() {
  const { user, loading } = useAuth();
  if (loading) return <div>loading</div>;
  return <div>{user ? `hi ${user.name}` : "anon"}</div>;
}

afterEach(() => vi.restoreAllMocks());

test("shows authenticated user from /bff/user", async () => {
  vi.spyOn(global, "fetch").mockResolvedValue(
    new Response(JSON.stringify({ isAuthenticated: true, name: "alice", roles: ["admin"], claims: [] }),
      { status: 200, headers: { "Content-Type": "application/json" } }));

  render(<AuthProvider><Probe /></AuthProvider>);
  await waitFor(() => expect(screen.getByText("hi alice")).toBeInTheDocument());
});

test("shows anon when /bff/user returns 401", async () => {
  vi.spyOn(global, "fetch").mockResolvedValue(new Response(null, { status: 401 }));
  render(<AuthProvider><Probe /></AuthProvider>);
  await waitFor(() => expect(screen.getByText("anon")).toBeInTheDocument());
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npx vitest run`
Expected: FAIL — `AuthContext`/`useAuth` modules do not exist.

- [ ] **Step 3: Write the auth context and hook**

`src/web/src/auth/AuthContext.tsx`:
```tsx
import { createContext, useEffect, useState, type ReactNode } from "react";

export interface User {
  isAuthenticated: boolean;
  name: string;
  roles: string[];
  claims: { type: string; value: string }[];
}

export interface AuthState {
  user: User | null;
  loading: boolean;
  login: () => void;
  logout: () => void;
}

export const AuthCtx = createContext<AuthState | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetch("/bff/user", { credentials: "include" })
      .then((r) => (r.ok ? (r.json() as Promise<User>) : null))
      .then(setUser)
      .catch(() => setUser(null))
      .finally(() => setLoading(false));
  }, []);

  const login = () =>
    (window.location.href = `/bff/login?returnUrl=${encodeURIComponent(window.location.pathname)}`);
  const logout = () => (window.location.href = "/bff/logout");

  return <AuthCtx.Provider value={{ user, loading, login, logout }}>{children}</AuthCtx.Provider>;
}
```

`src/web/src/auth/useAuth.ts`:
```ts
import { useContext } from "react";
import { AuthCtx, type AuthState } from "./AuthContext";

export function useAuth(): AuthState {
  const ctx = useContext(AuthCtx);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npx vitest run`
Expected: PASS — both tests green.

- [ ] **Step 5: Write the Profile component**

`src/web/src/components/Profile.tsx`:
```tsx
import { useAuth } from "../auth/useAuth";

export function Profile() {
  const { user } = useAuth();
  if (!user) return null;
  return (
    <section>
      <h2>Profile</h2>
      <p><strong>Name:</strong> {user.name}</p>
      <p><strong>Roles:</strong> {user.roles.join(", ") || "(none)"}</p>
      <details>
        <summary>All claims</summary>
        <ul>
          {user.claims.map((c, i) => (
            <li key={i}><code>{c.type}</code>: {c.value}</li>
          ))}
        </ul>
      </details>
    </section>
  );
}
```

- [ ] **Step 6: Wire App.tsx and main.tsx**

`src/web/src/main.tsx`:
```tsx
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { AuthProvider } from "./auth/AuthContext";
import App from "./App";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <AuthProvider>
      <App />
    </AuthProvider>
  </StrictMode>
);
```

`src/web/src/App.tsx`:
```tsx
import { useAuth } from "./auth/useAuth";
import { Profile } from "./components/Profile";

export default function App() {
  const { user, loading, login, logout } = useAuth();
  if (loading) return <p>Loading…</p>;

  return (
    <main style={{ fontFamily: "system-ui", maxWidth: 720, margin: "2rem auto" }}>
      <h1>Woodgrove Auth Demo</h1>
      {user ? (
        <>
          <button onClick={logout}>Log out</button>
          <Profile />
        </>
      ) : (
        <button onClick={login}>Log in with Keycloak</button>
      )}
    </main>
  );
}
```

- [ ] **Step 7: Manually verify end-to-end login in the browser**

Run: `dotnet run --project src/AppHost`. Open the `web` endpoint. Click "Log in", authenticate as `alice`, confirm the Profile shows name `alice` and roles including `admin`. Click "Log out" and confirm it returns to the logged-out state. Stop with Ctrl+C.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: React auth context, login/logout, and claims viewer"
```

---

### Task 8: Role-gated admin section + dev token-inspection panel

**Files:**
- Create: `src/web/src/components/AdminSection.tsx`, `src/web/src/components/TokenPanel.tsx`
- Modify: `src/web/src/App.tsx`, `src/WebBff/Program.cs`

**Interfaces:**
- Consumes: `useAuth()` (Task 7), `apiGet` (Task 6); API `/api/admin` (Task 3).
- Produces: admin UI calling `/api/admin` only for users with role `admin`; dev-only BFF endpoint `GET /bff/debug/tokens` returning decoded token info; `TokenPanel` rendering it.

- [ ] **Step 1: Write the AdminSection component**

`src/web/src/components/AdminSection.tsx`:
```tsx
import { useState } from "react";
import { useAuth } from "../auth/useAuth";
import { apiGet } from "../api/client";

export function AdminSection() {
  const { user } = useAuth();
  const [result, setResult] = useState<string>("");
  const [error, setError] = useState<string>("");

  if (!user?.roles.includes("admin")) {
    return <p><em>Admin section hidden — requires the <code>admin</code> role.</em></p>;
  }

  const callAdmin = async () => {
    setError(""); setResult("");
    try {
      const data = await apiGet<{ message: string }>("/api/admin");
      setResult(data.message);
    } catch (e) {
      setError((e as Error).message);
    }
  };

  return (
    <section>
      <h2>Admin</h2>
      <button onClick={callAdmin}>Call /api/admin</button>
      {result && <p>✅ {result}</p>}
      {error && <p>❌ {error}</p>}
    </section>
  );
}
```

- [ ] **Step 2: Add the dev-only token-inspection endpoint to the BFF**

In `src/WebBff/Program.cs`, add before `app.MapReverseProxy();`:
```csharp
if (app.Environment.IsDevelopment())
{
    app.MapGet("/bff/debug/tokens", async (HttpContext ctx) =>
    {
        if (ctx.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();

        static object Decode(string? jwt)
        {
            if (string.IsNullOrEmpty(jwt)) return new { present = false };
            var parts = jwt.Split('.');
            if (parts.Length < 2) return new { present = true, decoded = "(opaque)" };
            string Pad(string s) => s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=')
                .Replace('-', '+').Replace('_', '/');
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Pad(parts[1])));
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return new { present = true, payload = doc.RootElement.Clone() };
        }

        return Results.Ok(new
        {
            id_token = Decode(await ctx.GetTokenAsync("id_token")),
            access_token = Decode(await ctx.GetTokenAsync("access_token")),
            expires_at = await ctx.GetTokenAsync("expires_at")
        });
    });
}
```

- [ ] **Step 3: Write the TokenPanel component**

`src/web/src/components/TokenPanel.tsx`:
```tsx
import { useState } from "react";
import { apiGet } from "../api/client";

export function TokenPanel() {
  const [data, setData] = useState<unknown>(null);
  const [error, setError] = useState("");

  const load = async () => {
    setError("");
    try {
      setData(await apiGet("/bff/debug/tokens"));
    } catch (e) {
      setError((e as Error).message);
    }
  };

  return (
    <section>
      <h2>Token inspector (dev)</h2>
      <button onClick={load}>Inspect decoded tokens</button>
      {error && <p>❌ {error}</p>}
      {data != null && <pre style={{ overflow: "auto" }}>{JSON.stringify(data, null, 2)}</pre>}
    </section>
  );
}
```

- [ ] **Step 4: Render both in App.tsx (authenticated only)**

In `src/web/src/App.tsx`, add imports and render inside the `user ?` branch after `<Profile />`:
```tsx
import { AdminSection } from "./components/AdminSection";
import { TokenPanel } from "./components/TokenPanel";
```
```tsx
          <Profile />
          <AdminSection />
          <TokenPanel />
```

- [ ] **Step 5: Manually verify role gating and token inspection**

Run: `dotnet run --project src/AppHost`.
1. Log in as `bob` → admin section shows the "hidden" message; calling `/api/admin` is not offered.
2. Log in as `alice` → admin section appears; "Call /api/admin" returns `secret admin data`.
3. Click "Inspect decoded tokens" → see decoded ID and access token payloads (note `roles`, `aud: woodgrove-api`, `exp`).
Stop with Ctrl+C.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: role-gated admin section and dev token-inspection panel"
```

---

### Task 9: Cheatsheet

**Files:**
- Create: `docs/cheatsheet.md`

**Interfaces:** none (documentation).

- [ ] **Step 1: Write the cheatsheet**

Create `docs/cheatsheet.md` covering, with concrete examples drawn from this project:
- OAuth2 roles (resource owner, client, authorization server = Keycloak, resource server = API) and grant types (focus: Authorization Code + PKCE; mention client credentials, device code).
- OIDC vs OAuth2: authentication vs authorization; the ID token vs access token vs refresh token; JWT anatomy (`header.payload.signature`) and key claims (`iss`, `aud`, `exp`, `sub`, `preferred_username`, `roles`).
- Authorization Code + PKCE sequence diagram (browser → BFF → Keycloak → BFF token exchange → cookie).
- SSO: shared Keycloak session, how a second app would log in silently, end-session/single logout.
- BFF pattern: why tokens stay server-side; cookie (HttpOnly) vs browser token storage trade-offs; the YARP token-attach proxy.
- Keycloak concepts: realm, client (confidential `web-bff` vs public vs bearer-only), client scopes, realm roles, protocol mappers (audience mapper, realm-role mapper), where roles land in the token.
- Aspire concepts used: AppHost, resources, `WithReference`/`WaitFor`, `WithRealmImport`, service discovery, `AddNpmApp`.
- Key .NET APIs: cookie + OIDC handlers, `AddKeycloakOpenIdConnect`, `AddKeycloakJwtBearer`, `[Authorize]`/`RequireRole`, `OnValidatePrincipal` refresh.
- Common pitfalls (each with the symptom): audience mismatch (`401`/`invalid token`), redirect URI not registered, HTTPS metadata in dev, clock skew, role-claim type mismatch, `dev-bff-secret` is dev-only.
- Handy snippets: discovery doc URL `http://localhost:8080/realms/woodgrove/.well-known/openid-configuration`; a `curl` password-grant token request for quick API testing; how to decode a JWT.

- [ ] **Step 2: Commit**

```bash
git add -A
git commit -m "docs: add OAuth/OIDC/SSO + Keycloak/Aspire cheatsheet"
```

---

### Task 10: README + manual verification checklist + final review

**Files:**
- Create: `README.md`
- Modify: none (verification task)

**Interfaces:** none.

- [ ] **Step 1: Write the README**

Create `README.md` with: prerequisites (Docker running, .NET 11 preview SDK, Node), one-command run (`dotnet run --project src/AppHost`), the resource map, test-user table (`alice`/`bob`, password `password`), where the cheatsheet lives, and a manual verification checklist:
- [ ] Dashboard shows `keycloak`, `api`, `webbff`, `web` healthy.
- [ ] `/bff/user` is 401 before login.
- [ ] Login as `alice` succeeds; Profile shows `admin` role.
- [ ] `/api/admin` allowed for `alice`, denied (403) for `bob`.
- [ ] Token inspector shows `aud: woodgrove-api` and `roles`.
- [ ] Access token refreshes transparently after expiry.
- [ ] Logout clears session; `/bff/user` is 401 again.

- [ ] **Step 2: Run the full test suite**

Run:
```bash
dotnet test
cd src/web && npx vitest run
```
Expected: all .NET tests pass; all Vitest tests pass.

- [ ] **Step 3: Full manual smoke test**

Run `dotnet run --project src/AppHost` and walk the README checklist end-to-end. Fix any gaps before final commit.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "docs: add README and verification checklist"
```

---

## Self-Review

**Spec coverage:**
- Architecture/tiers → Tasks 1–6. ✅
- Keycloak realm auto-import (roles, clients, audience, users) → Task 2. ✅
- BFF cookie + OIDC + PKCE + token custody → Task 4. ✅
- YARP proxy with bearer attach → Task 4. ✅
- Token refresh → Task 5. ✅
- React 19 SPA, cookie-based, no tokens in JS → Tasks 6–8. ✅
- Login/logout + claims viewer → Task 7. ✅
- Role-based authorization (API + UI) → Tasks 3, 8. ✅
- Token inspection panel (dev-gated, decoded) → Task 8. ✅
- Cheatsheet → Task 9. ✅
- Testing (API integration, BFF smoke via /bff/user 401, manual checklist) → Tasks 3, 4, 7, 10. ✅
- Success criteria → Task 10 checklist. ✅

**Placeholder scan:** No "TBD"/"handle edge cases"/"similar to Task N" — code is inline in each step. ✅

**Type consistency:** `roles` claim name, `RolesClaimsHelper.RoleClaimType`/`NameClaimType`, `User` shape (`isAuthenticated/name/roles/claims`), `apiGet`, `useAuth` return shape, and BFF endpoint paths (`/bff/user`, `/bff/login`, `/bff/logout`, `/bff/debug/tokens`) are consistent across tasks. ✅

**Note for implementer:** Aspire's `AddNpmApp`/Vite port wiring (Task 6) and the exact prerelease package versions (Task 4 `Microsoft.Extensions.ServiceDiscovery.Yarp`) may need a minor adjustment against the installed Aspire build; the token inspector (Task 8) is the tool for verifying claim/audience/role mapping if anything looks off.
