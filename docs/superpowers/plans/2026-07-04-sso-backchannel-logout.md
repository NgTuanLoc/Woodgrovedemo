# Second App SSO + Back-Channel Single Logout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a second OIDC client app (Razor Pages "Intranet") so silent SSO login is actually observable, and implement OIDC Back-Channel Logout in both directions so logging out of either app kills the session in both.

**Architecture:** A shared `AuthShared` library holds the logout-token validator, an in-memory `sid` denylist, a back-channel endpoint mapper, and a cookie event that rejects denylisted sessions. Both `WebBff` and the new `Intranet` app consume it. Keycloak POSTs signed `logout_token`s to pinned host ports via `host.docker.internal`.

**Tech Stack:** .NET 11 (net11.0), Aspire (`Aspire.Keycloak.Authentication` 13.4.6-preview.1.26319.6), `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.9, Razor Pages, xUnit + `Microsoft.AspNetCore.Mvc.Testing` 10.0.9, Keycloak container.

## Global Constraints

- Target framework: `net11.0` for all .NET projects.
- Realm: `woodgrove`. Existing client: `web-bff` (secret `dev-bff-secret`). New client: `intranet`, secret `dev-intranet-secret` (**DEV ONLY** — documented pitfall, same as `dev-bff-secret`).
- Pinned dev ports: WebBff HTTP `5242` (already pinned in launchSettings), Intranet HTTP `5262` (new), Vite web `5173` (pinned in this plan). Ports `5072`, `7028`, `7228`, `15173`, `17243` are taken — do not use.
- Back-channel URLs registered in the realm: `http://host.docker.internal:5242/bff/backchannel-logout` (web-bff) and `http://host.docker.internal:5262/auth/backchannel-logout` (intranet).
- Cookie names: `Woodgrove.Bff` (existing), `Woodgrove.Intranet` (new). Both 8h lifetime; denylist TTL must equal cookie lifetime.
- Logout-token validation follows OIDC Back-Channel Logout 1.0: signature+iss+aud+lifetime, `events` must contain `http://schemas.openid.net/event/backchannel-logout`, `sid` required, `nonce` forbidden.
- Back-channel endpoint responses: `200` empty body on success, `400` on any failure; failure reasons logged server-side only, never echoed.
- Keycloak only re-imports the realm on a **fresh data volume** — Task 4 includes the reset procedure. Don't skip it or the `intranet` client won't exist.
- `dotnet run --project src/AppHost` stays the single entry point.
- Commit after every task. Conventional commit messages. The cheatsheet task (Task 5) is **mandatory** — the user learns from `docs/cheatsheet.md`.

---

## File Structure

```
src/
  AuthShared/                      NEW class library (shared by WebBff + Intranet)
    AuthShared.csproj
    SessionDenylist.cs             ISessionDenylist + MemorySessionDenylist + DI extension
    LogoutTokenValidator.cs        Spec-compliant logout_token validation (pure, testable)
    BackchannelLogoutEndpoint.cs   MapBackchannelLogout() endpoint extension
    DenylistCookieEvents.cs        OnValidatePrincipal helper rejecting revoked sids
    ReturnUrl.cs                   SafeReturnUrl logic extracted from WebBff (DRY)
  Intranet/                        NEW Razor Pages app (second OIDC client)
    Intranet.csproj
    Program.cs
    Properties/launchSettings.json (pinned http://localhost:5262)
    appsettings.json
    Pages/_ViewImports.cshtml
    Pages/Index.cshtml
  WebBff/
    Program.cs                     MODIFY: denylist + backchannel endpoint + ReturnUrl + partial Program
    WebBff.csproj                  MODIFY: reference AuthShared
  AppHost/
    Program.cs                     MODIFY: intranet resource, pin web port 5173
    AppHost.csproj                 MODIFY: reference Intranet
  web/src/App.tsx                  MODIFY: link to Intranet
keycloak/woodgrove-realm.json      MODIFY: intranet client + backchannel attrs on both clients
tests/
  AuthShared.Tests/                NEW: validator + denylist + ReturnUrl unit tests, TestTokens helper
  WebBff.Tests/                    NEW: BFF smoke + backchannel integration tests
  Intranet.Tests/                  NEW: Intranet smoke + backchannel integration tests
docs/
  cheatsheet.md                    MODIFY: §4 rewritten, §9 new pitfalls
  README.md                        MODIFY: resource table, checklist, architecture
```

---

### Task 1: AuthShared library + unit tests

**Files:**
- Create: `src/AuthShared/AuthShared.csproj`, `src/AuthShared/SessionDenylist.cs`, `src/AuthShared/LogoutTokenValidator.cs`, `src/AuthShared/ReturnUrl.cs`, `src/AuthShared/BackchannelLogoutEndpoint.cs`, `src/AuthShared/DenylistCookieEvents.cs`
- Test: `tests/AuthShared.Tests/AuthShared.Tests.csproj`, `tests/AuthShared.Tests/TestTokens.cs`, `tests/AuthShared.Tests/LogoutTokenValidatorTests.cs`, `tests/AuthShared.Tests/SessionDenylistTests.cs`, `tests/AuthShared.Tests/ReturnUrlTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces (used by Tasks 2–3):
  - `AuthShared.ISessionDenylist` with `void Revoke(string sid)` / `bool IsRevoked(string sid)`; DI via `services.AddSessionDenylist(TimeSpan ttl)`.
  - `AuthShared.LogoutTokenValidator.ValidateAsync(string logoutToken, TokenValidationParameters tvp)` → `LogoutTokenValidationResult(bool IsValid, string? Sid, string? Error)`; constant `LogoutTokenValidator.BackchannelLogoutEvent`.
  - `AuthShared.BackchannelLogoutEndpoint.MapBackchannelLogout(this IEndpointRouteBuilder, string path, string oidcScheme = OpenIdConnectDefaults.AuthenticationScheme)`.
  - `AuthShared.DenylistCookieEvents.RejectIfRevokedAsync(CookieValidatePrincipalContext)`.
  - `AuthShared.ReturnUrl.Sanitize(string? url)` → `string` (local URL or `"/"`).
  - Test helper (used by Tasks 2–3 test projects via ProjectReference to AuthShared.Tests): `AuthShared.Tests.TestTokens` — `SigningKey`, `Issuer` (`https://keycloak.test/realms/woodgrove`), `CreateLogoutToken(...)`, `Tvp(...)`.

- [ ] **Step 1: Create the projects and wire references**

```bash
cd "D:/Study/AspNetCore/Woodgrovedemo"
dotnet new classlib -n AuthShared -o src/AuthShared -f net11.0
dotnet new xunit -n AuthShared.Tests -o tests/AuthShared.Tests -f net11.0
dotnet add tests/AuthShared.Tests/AuthShared.Tests.csproj reference src/AuthShared/AuthShared.csproj
dotnet sln add src/AuthShared/AuthShared.csproj tests/AuthShared.Tests/AuthShared.Tests.csproj
```

Then delete the generated `src/AuthShared/Class1.cs` and `tests/AuthShared.Tests/UnitTest1.cs`.

Replace `src/AuthShared/AuthShared.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="10.0.9" />
  </ItemGroup>

</Project>
```

Edit `tests/AuthShared.Tests/AuthShared.Tests.csproj` so it matches the shape of `tests/Api.Tests/Api.Tests.csproj` (same package versions), referencing AuthShared:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\AuthShared\AuthShared.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the failing tests**

`tests/AuthShared.Tests/TestTokens.cs` (also consumed by WebBff.Tests / Intranet.Tests later — keep names exactly as written):

```csharp
using System.Security.Cryptography;
using AuthShared;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AuthShared.Tests;

public static class TestTokens
{
    public static readonly RsaSecurityKey SigningKey =
        new(RSA.Create(2048)) { KeyId = "test-key" };

    public const string Issuer = "https://keycloak.test/realms/woodgrove";
    public const string Audience = "web-bff";

    public static string CreateLogoutToken(
        string issuer = Issuer,
        string audience = Audience,
        string? sid = "sess-123",
        bool includeEvents = true,
        string? nonce = null,
        SecurityKey? signingKey = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = "user-1" };
        if (sid is not null) claims["sid"] = sid;
        if (includeEvents)
            claims["events"] = new Dictionary<string, object>
            {
                [LogoutTokenValidator.BackchannelLogoutEvent] = new Dictionary<string, object>()
            };
        if (nonce is not null) claims["nonce"] = nonce;

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(2),
            SigningCredentials = new SigningCredentials(
                signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256)
        });
    }

    public static TokenValidationParameters Tvp(string audience = Audience) => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = audience,
        IssuerSigningKey = SigningKey
    };
}
```

`tests/AuthShared.Tests/LogoutTokenValidatorTests.cs`:

```csharp
using System.Security.Cryptography;
using AuthShared;
using Microsoft.IdentityModel.Tokens;

namespace AuthShared.Tests;

public class LogoutTokenValidatorTests
{
    [Fact]
    public async Task Valid_token_returns_sid()
    {
        var result = await LogoutTokenValidator.ValidateAsync(
            TestTokens.CreateLogoutToken(), TestTokens.Tvp());

        Assert.True(result.IsValid);
        Assert.Equal("sess-123", result.Sid);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Wrong_audience_is_rejected()
    {
        var result = await LogoutTokenValidator.ValidateAsync(
            TestTokens.CreateLogoutToken(audience: "someone-else"), TestTokens.Tvp());
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Wrong_issuer_is_rejected()
    {
        var result = await LogoutTokenValidator.ValidateAsync(
            TestTokens.CreateLogoutToken(issuer: "https://evil.test"), TestTokens.Tvp());
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Wrong_signing_key_is_rejected()
    {
        var otherKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "other-key" };
        var result = await LogoutTokenValidator.ValidateAsync(
            TestTokens.CreateLogoutToken(signingKey: otherKey), TestTokens.Tvp());
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Missing_events_claim_is_rejected()
    {
        var result = await LogoutTokenValidator.ValidateAsync(
            TestTokens.CreateLogoutToken(includeEvents: false), TestTokens.Tvp());
        Assert.False(result.IsValid);
        Assert.Contains("events", result.Error);
    }

    [Fact]
    public async Task Nonce_present_is_rejected()
    {
        var result = await LogoutTokenValidator.ValidateAsync(
            TestTokens.CreateLogoutToken(nonce: "abc"), TestTokens.Tvp());
        Assert.False(result.IsValid);
        Assert.Contains("nonce", result.Error);
    }

    [Fact]
    public async Task Missing_sid_is_rejected()
    {
        var result = await LogoutTokenValidator.ValidateAsync(
            TestTokens.CreateLogoutToken(sid: null), TestTokens.Tvp());
        Assert.False(result.IsValid);
        Assert.Contains("sid", result.Error);
    }

    [Fact]
    public async Task Garbage_string_is_rejected()
    {
        var result = await LogoutTokenValidator.ValidateAsync(
            "not-a-jwt", TestTokens.Tvp());
        Assert.False(result.IsValid);
    }
}
```

`tests/AuthShared.Tests/SessionDenylistTests.cs`:

```csharp
using AuthShared;
using Microsoft.Extensions.Caching.Memory;

namespace AuthShared.Tests;

public class SessionDenylistTests
{
    private static MemorySessionDenylist NewDenylist() =>
        new(new MemoryCache(new MemoryCacheOptions()), TimeSpan.FromHours(8));

    [Fact]
    public void Revoked_sid_is_reported_revoked()
    {
        var denylist = NewDenylist();
        denylist.Revoke("sess-123");
        Assert.True(denylist.IsRevoked("sess-123"));
    }

    [Fact]
    public void Unknown_sid_is_not_revoked()
    {
        Assert.False(NewDenylist().IsRevoked("sess-999"));
    }
}
```

`tests/AuthShared.Tests/ReturnUrlTests.cs`:

```csharp
using AuthShared;

namespace AuthShared.Tests;

public class ReturnUrlTests
{
    [Theory]
    [InlineData("/dashboard", "/dashboard")]
    [InlineData("/", "/")]
    [InlineData("~/settings", "~/settings")]
    [InlineData("//evil.com", "/")]
    [InlineData("/\\evil.com", "/")]
    [InlineData("https://evil.com", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    public void Sanitize_only_allows_local_urls(string? input, string expected)
    {
        Assert.Equal(expected, ReturnUrl.Sanitize(input));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/AuthShared.Tests`
Expected: FAIL — compile errors: `LogoutTokenValidator`, `MemorySessionDenylist`, `ReturnUrl` do not exist.

- [ ] **Step 4: Implement the library**

`src/AuthShared/SessionDenylist.cs`:

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace AuthShared;

/// <summary>Tracks Keycloak session ids (sid) revoked via back-channel logout.</summary>
public interface ISessionDenylist
{
    void Revoke(string sid);
    bool IsRevoked(string sid);
}

// In-memory, single-instance only — fine for this dev/learning setup.
// Production would use a distributed cache so all instances see revocations.
public sealed class MemorySessionDenylist(IMemoryCache cache, TimeSpan ttl) : ISessionDenylist
{
    public void Revoke(string sid) => cache.Set(Key(sid), true, ttl);
    public bool IsRevoked(string sid) => cache.TryGetValue(Key(sid), out _);
    private static string Key(string sid) => "revoked-sid:" + sid;
}

public static class SessionDenylistServiceCollectionExtensions
{
    /// <param name="ttl">Should match the auth cookie lifetime — an entry only
    /// needs to outlive every cookie that could carry its sid.</param>
    public static IServiceCollection AddSessionDenylist(this IServiceCollection services, TimeSpan ttl)
    {
        services.AddMemoryCache();
        services.AddSingleton<ISessionDenylist>(sp =>
            new MemorySessionDenylist(sp.GetRequiredService<IMemoryCache>(), ttl));
        return services;
    }
}
```

`src/AuthShared/LogoutTokenValidator.cs`:

```csharp
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AuthShared;

public sealed record LogoutTokenValidationResult(bool IsValid, string? Sid, string? Error)
{
    public static LogoutTokenValidationResult Success(string sid) => new(true, sid, null);
    public static LogoutTokenValidationResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// Validates an OIDC Back-Channel Logout 1.0 logout_token:
/// signature/issuer/audience/lifetime via TokenValidationParameters, then the
/// spec's claim rules: events must contain the backchannel-logout event,
/// sid must be present, nonce must NOT be present.
/// </summary>
public static class LogoutTokenValidator
{
    public const string BackchannelLogoutEvent =
        "http://schemas.openid.net/event/backchannel-logout";

    public static async Task<LogoutTokenValidationResult> ValidateAsync(
        string logoutToken, TokenValidationParameters validationParameters)
    {
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(logoutToken, validationParameters);
        if (!result.IsValid)
            return LogoutTokenValidationResult.Fail(
                "token validation failed: " + (result.Exception?.Message ?? "unknown"));

        var jwt = (JsonWebToken)result.SecurityToken;
        using var payload = JsonDocument.Parse(Base64UrlEncoder.Decode(jwt.EncodedPayload));
        var root = payload.RootElement;

        if (root.TryGetProperty("nonce", out _))
            return LogoutTokenValidationResult.Fail("nonce must not be present in a logout token");

        if (!root.TryGetProperty("events", out var events)
            || events.ValueKind != JsonValueKind.Object
            || !events.TryGetProperty(BackchannelLogoutEvent, out _))
            return LogoutTokenValidationResult.Fail("events claim missing backchannel-logout event");

        if (!root.TryGetProperty("sid", out var sidElement)
            || sidElement.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(sidElement.GetString()))
            return LogoutTokenValidationResult.Fail("sid claim missing");

        return LogoutTokenValidationResult.Success(sidElement.GetString()!);
    }
}
```

`src/AuthShared/ReturnUrl.cs` (logic moved verbatim from `SafeReturnUrl` in `src/WebBff/Program.cs:101-106`):

```csharp
namespace AuthShared;

public static class ReturnUrl
{
    /// <summary>
    /// Only allows relative, same-site return URLs to prevent open-redirect abuse
    /// (e.g. ?returnUrl=https://evil.com). Mirrors IUrlHelper.IsLocalUrl:
    /// must start with "/" (but not "//" or "/\") or "~/". Anything else → "/".
    /// </summary>
    public static string Sanitize(string? url) =>
        !string.IsNullOrEmpty(url)
        && ((url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\')))
            || (url.Length > 1 && url[0] == '~' && url[1] == '/'))
            ? url
            : "/";
}
```

`src/AuthShared/BackchannelLogoutEndpoint.cs`:

```csharp
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AuthShared;

public static class BackchannelLogoutEndpoint
{
    /// <summary>
    /// Maps the endpoint Keycloak POSTs logout_tokens to (form field "logout_token").
    /// 200 empty body on success, 400 on any failure. Failure details are logged
    /// server-side only — never echoed to the caller.
    /// </summary>
    public static IEndpointConventionBuilder MapBackchannelLogout(
        this IEndpointRouteBuilder endpoints,
        string path,
        string oidcScheme = OpenIdConnectDefaults.AuthenticationScheme)
    {
        return endpoints.MapPost(path, async (HttpContext ctx) =>
        {
            var log = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("AuthShared.BackchannelLogout");

            if (!ctx.Request.HasFormContentType)
            {
                log.LogWarning("Back-channel logout request without form content");
                return Results.BadRequest();
            }

            var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
            var token = form["logout_token"].ToString();
            if (string.IsNullOrEmpty(token))
            {
                log.LogWarning("Back-channel logout request without logout_token");
                return Results.BadRequest();
            }

            var oidc = ctx.RequestServices
                .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
                .Get(oidcScheme);
            if (oidc.ConfigurationManager is null)
            {
                log.LogWarning("No OIDC ConfigurationManager for scheme {Scheme}", oidcScheme);
                return Results.BadRequest();
            }

            TokenValidationParameters validationParameters;
            try
            {
                var config = await oidc.ConfigurationManager
                    .GetConfigurationAsync(ctx.RequestAborted);
                validationParameters = new TokenValidationParameters
                {
                    ValidIssuer = config.Issuer,
                    ValidAudience = oidc.ClientId,
                    IssuerSigningKeys = config.SigningKeys
                };
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Could not load OIDC configuration to validate logout token");
                return Results.BadRequest();
            }

            var result = await LogoutTokenValidator.ValidateAsync(token, validationParameters);
            if (!result.IsValid)
            {
                log.LogWarning("Rejected logout token: {Reason}", result.Error);
                return Results.BadRequest();
            }

            ctx.RequestServices.GetRequiredService<ISessionDenylist>().Revoke(result.Sid!);
            log.LogInformation("Back-channel logout: revoked Keycloak session {Sid}", result.Sid);
            return Results.Ok();
        }).AllowAnonymous();
    }
}
```

`src/AuthShared/DenylistCookieEvents.cs`:

```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace AuthShared;

public static class DenylistCookieEvents
{
    /// <summary>
    /// Rejects the cookie principal when its Keycloak session id (sid) was revoked
    /// via back-channel logout. Run this FIRST when composing OnValidatePrincipal.
    /// A principal without a sid claim (cookie issued before this feature) is left
    /// alone — documented pitfall.
    /// </summary>
    public static Task RejectIfRevokedAsync(CookieValidatePrincipalContext context)
    {
        var sid = context.Principal?.FindFirst("sid")?.Value;
        if (string.IsNullOrEmpty(sid)) return Task.CompletedTask;

        var denylist = context.HttpContext.RequestServices
            .GetRequiredService<ISessionDenylist>();
        if (denylist.IsRevoked(sid))
            context.RejectPrincipal(); // sets Principal = null → treated as anonymous

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/AuthShared.Tests`
Expected: PASS — 18 test cases green (8 validator facts, 2 denylist facts, 8 ReturnUrl theory cases).

- [ ] **Step 6: Commit**

```bash
git add src/AuthShared tests/AuthShared.Tests Woodgrovedemo.slnx
git commit -m "feat: AuthShared library - logout-token validation, sid denylist, returnUrl sanitizer"
```

---

### Task 2: Wire back-channel logout into WebBff + WebBff.Tests

**Files:**
- Modify: `src/WebBff/Program.cs`, `src/WebBff/WebBff.csproj`
- Test: `tests/WebBff.Tests/WebBff.Tests.csproj`, `tests/WebBff.Tests/BffTestFactory.cs`, `tests/WebBff.Tests/BffEndpointTests.cs`

**Interfaces:**
- Consumes (Task 1): `AddSessionDenylist(TimeSpan)`, `MapBackchannelLogout(path)`, `DenylistCookieEvents.RejectIfRevokedAsync`, `ReturnUrl.Sanitize`, `ISessionDenylist`, `AuthShared.Tests.TestTokens` (`Issuer`, `SigningKey`, `CreateLogoutToken`).
- Produces: `POST /bff/backchannel-logout` on the BFF; `public partial class Program` in WebBff (needed by `WebApplicationFactory<Program>`); pattern that Task 3 mirrors.

- [ ] **Step 1: Create the test project**

```bash
cd "D:/Study/AspNetCore/Woodgrovedemo"
dotnet new xunit -n WebBff.Tests -o tests/WebBff.Tests -f net11.0
dotnet add tests/WebBff.Tests/WebBff.Tests.csproj reference src/WebBff/WebBff.csproj tests/AuthShared.Tests/AuthShared.Tests.csproj
dotnet add tests/WebBff.Tests/WebBff.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 10.0.9
dotnet sln add tests/WebBff.Tests/WebBff.Tests.csproj
```

Delete the generated `tests/WebBff.Tests/UnitTest1.cs`. Ensure the csproj ends up equivalent to `tests/Api.Tests/Api.Tests.csproj` (same `PackageReference` versions, `<Using Include="Xunit" />`) plus the two `ProjectReference`s above.

- [ ] **Step 2: Write the failing tests**

`tests/WebBff.Tests/BffTestFactory.cs` — replaces live Keycloak discovery with a static OIDC configuration signed by the test key, so no network is needed:

```csharp
using AuthShared.Tests;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace WebBff.Tests;

public class BffTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<OpenIdConnectOptions>(
                OpenIdConnectDefaults.AuthenticationScheme, options =>
                {
                    var config = new OpenIdConnectConfiguration
                    {
                        Issuer = TestTokens.Issuer,
                        AuthorizationEndpoint =
                            TestTokens.Issuer + "/protocol/openid-connect/auth",
                        TokenEndpoint =
                            TestTokens.Issuer + "/protocol/openid-connect/token",
                        EndSessionEndpoint =
                            TestTokens.Issuer + "/protocol/openid-connect/logout"
                    };
                    config.SigningKeys.Add(TestTokens.SigningKey);
                    options.Configuration = config;
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(config);
                });
        });
    }
}
```

`tests/WebBff.Tests/BffEndpointTests.cs`:

```csharp
using System.Net;
using AuthShared;
using AuthShared.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace WebBff.Tests;

public class BffEndpointTests : IClassFixture<BffTestFactory>
{
    private readonly BffTestFactory _factory;
    public BffEndpointTests(BffTestFactory factory) => _factory = factory;

    private HttpClient NoRedirectClient() => _factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    [Fact]
    public async Task Bff_user_returns_401_when_anonymous()
    {
        var response = await _factory.CreateClient().GetAsync("/bff/user");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bff_login_redirects_to_keycloak_authorize_endpoint()
    {
        var response = await NoRedirectClient().GetAsync("/bff/login");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("/protocol/openid-connect/auth", location);
        Assert.Contains("client_id=web-bff", location);
    }

    [Fact]
    public async Task Backchannel_logout_with_garbage_returns_400()
    {
        var response = await _factory.CreateClient().PostAsync("/bff/backchannel-logout",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["logout_token"] = "not-a-jwt"
            }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Backchannel_logout_with_valid_token_revokes_sid()
    {
        var response = await _factory.CreateClient().PostAsync("/bff/backchannel-logout",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                // audience must match the BFF's ClientId from appsettings: web-bff
                ["logout_token"] = TestTokens.CreateLogoutToken(
                    audience: "web-bff", sid: "sess-bff-1")
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var denylist = _factory.Services.GetRequiredService<ISessionDenylist>();
        Assert.True(denylist.IsRevoked("sess-bff-1"));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/WebBff.Tests`
Expected: FAIL — `Program` is inaccessible (no `public partial class Program` in WebBff yet) / 404 on `/bff/backchannel-logout`.

- [ ] **Step 4: Modify WebBff**

```bash
dotnet add src/WebBff/WebBff.csproj reference src/AuthShared/AuthShared.csproj
```

In `src/WebBff/Program.cs`, make these five edits:

1. Add to the usings at the top:

```csharp
using AuthShared;
```

2. After `builder.AddServiceDefaults();`, add:

```csharp
// One lifetime for both: a denylist entry only needs to outlive the cookies
// that can carry its sid.
var sessionLifetime = TimeSpan.FromHours(8);
builder.Services.AddSessionDenylist(sessionLifetime);
```

3. In `.AddCookie(...)`, change `options.ExpireTimeSpan = TimeSpan.FromHours(8);` to `options.ExpireTimeSpan = sessionLifetime;` and replace the `options.Events = ...` block with:

```csharp
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                // Back-channel logout check first: if the Keycloak session was
                // revoked, don't bother refreshing tokens for it.
                await DenylistCookieEvents.RejectIfRevokedAsync(context);
                if (context.Principal is not null)
                    await TokenRefresher.ValidateAsync(context);
            }
        };
```

4. Delete the local `static string SafeReturnUrl(...)` function (lines 98–106) and replace its two call sites with `ReturnUrl.Sanitize(returnUrl)`:

```csharp
app.MapGet("/bff/login", (string? returnUrl) =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = ReturnUrl.Sanitize(returnUrl) }))
    .AllowAnonymous();

app.MapGet("/bff/logout", (string? returnUrl) =>
    Results.SignOut(new AuthenticationProperties { RedirectUri = ReturnUrl.Sanitize(returnUrl) },
        new[] { CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme }));
```

5. Before `app.MapReverseProxy();`, add the endpoint; after `app.Run();`, add the partial class:

```csharp
// Keycloak POSTs signed logout_tokens here when the SSO session ends
// (registered in keycloak/woodgrove-realm.json as backchannel.logout.url).
app.MapBackchannelLogout("/bff/backchannel-logout");
```

```csharp
public partial class Program { } // for WebApplicationFactory in tests
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/WebBff.Tests && dotnet test tests/Api.Tests && dotnet test tests/AuthShared.Tests`
Expected: PASS — 4 new BFF tests; Api and AuthShared suites still green.

- [ ] **Step 6: Commit**

```bash
git add src/WebBff tests/WebBff.Tests Woodgrovedemo.slnx
git commit -m "feat(bff): back-channel logout endpoint and sid-denylist session revocation"
```

---

### Task 3: Intranet Razor Pages app + Intranet.Tests

**Files:**
- Create: `src/Intranet/Intranet.csproj`, `src/Intranet/Program.cs`, `src/Intranet/Properties/launchSettings.json`, `src/Intranet/appsettings.json`, `src/Intranet/Pages/_ViewImports.cshtml`, `src/Intranet/Pages/Index.cshtml`
- Test: `tests/Intranet.Tests/Intranet.Tests.csproj`, `tests/Intranet.Tests/IntranetTestFactory.cs`, `tests/Intranet.Tests/IntranetEndpointTests.cs`

**Interfaces:**
- Consumes (Task 1): `AddSessionDenylist`, `MapBackchannelLogout`, `DenylistCookieEvents.RejectIfRevokedAsync`, `ReturnUrl.Sanitize`, `TestTokens`.
- Produces: Aspire-ready `Intranet` project (registered in AppHost in Task 4); endpoints `GET /` (public page), `GET /auth/login`, `GET /auth/logout`, `POST /auth/backchannel-logout`; pinned URL `http://localhost:5262`; Keycloak client id `intranet`.

- [ ] **Step 1: Create the projects**

```bash
cd "D:/Study/AspNetCore/Woodgrovedemo"
dotnet new web -n Intranet -o src/Intranet -f net11.0
dotnet add src/Intranet/Intranet.csproj reference src/ServiceDefaults/ServiceDefaults.csproj src/AuthShared/AuthShared.csproj
dotnet add src/Intranet/Intranet.csproj package Aspire.Keycloak.Authentication --version 13.4.6-preview.1.26319.6
dotnet add src/Intranet/Intranet.csproj package Microsoft.AspNetCore.Authentication.OpenIdConnect --version 10.0.9
dotnet new xunit -n Intranet.Tests -o tests/Intranet.Tests -f net11.0
dotnet add tests/Intranet.Tests/Intranet.Tests.csproj reference src/Intranet/Intranet.csproj tests/AuthShared.Tests/AuthShared.Tests.csproj
dotnet add tests/Intranet.Tests/Intranet.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 10.0.9
dotnet sln add src/Intranet/Intranet.csproj tests/Intranet.Tests/Intranet.Tests.csproj
```

Delete `tests/Intranet.Tests/UnitTest1.cs`. Align `Intranet.Tests.csproj` package versions with `tests/Api.Tests/Api.Tests.csproj` (`Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, `coverlet.collector` 6.0.4, `<Using Include="Xunit" />`).

- [ ] **Step 2: Pin the port and configure the client**

`src/Intranet/Properties/launchSettings.json` (replace whatever the template generated — the port MUST be 5262; it is baked into the realm's back-channel URL in Task 4):

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "http://localhost:5262",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

`src/Intranet/appsettings.json`:

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "Keycloak": { "ClientId": "intranet", "ClientSecret": "dev-intranet-secret" }
}
```

- [ ] **Step 3: Write the failing tests**

`tests/Intranet.Tests/IntranetTestFactory.cs` (same static-config trick as the BFF's):

```csharp
using AuthShared.Tests;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Intranet.Tests;

public class IntranetTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<OpenIdConnectOptions>(
                OpenIdConnectDefaults.AuthenticationScheme, options =>
                {
                    var config = new OpenIdConnectConfiguration
                    {
                        Issuer = TestTokens.Issuer,
                        AuthorizationEndpoint =
                            TestTokens.Issuer + "/protocol/openid-connect/auth",
                        TokenEndpoint =
                            TestTokens.Issuer + "/protocol/openid-connect/token",
                        EndSessionEndpoint =
                            TestTokens.Issuer + "/protocol/openid-connect/logout"
                    };
                    config.SigningKeys.Add(TestTokens.SigningKey);
                    options.Configuration = config;
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(config);
                });
        });
    }
}
```

`tests/Intranet.Tests/IntranetEndpointTests.cs`:

```csharp
using System.Net;
using AuthShared;
using AuthShared.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Intranet.Tests;

public class IntranetEndpointTests : IClassFixture<IntranetTestFactory>
{
    private readonly IntranetTestFactory _factory;
    public IntranetEndpointTests(IntranetTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Index_is_public_and_returns_200()
    {
        var response = await _factory.CreateClient().GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Woodgrove Intranet", html);
    }

    [Fact]
    public async Task Login_redirects_to_keycloak_authorize_endpoint()
    {
        var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        var response = await client.GetAsync("/auth/login");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("/protocol/openid-connect/auth", location);
        Assert.Contains("client_id=intranet", location);
    }

    [Fact]
    public async Task Backchannel_logout_with_garbage_returns_400()
    {
        var response = await _factory.CreateClient().PostAsync("/auth/backchannel-logout",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["logout_token"] = "not-a-jwt"
            }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Backchannel_logout_with_valid_token_revokes_sid()
    {
        var response = await _factory.CreateClient().PostAsync("/auth/backchannel-logout",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                // audience must match this app's ClientId from appsettings: intranet
                ["logout_token"] = TestTokens.CreateLogoutToken(
                    audience: "intranet", sid: "sess-intranet-1")
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var denylist = _factory.Services.GetRequiredService<ISessionDenylist>();
        Assert.True(denylist.IsRevoked("sess-intranet-1"));
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/Intranet.Tests`
Expected: FAIL — `Program` inaccessible / endpoints and page not defined.

- [ ] **Step 5: Implement the Intranet app**

`src/Intranet/Program.cs`:

```csharp
using AuthShared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var sessionLifetime = TimeSpan.FromHours(8);
builder.Services.AddSessionDenylist(sessionLifetime);
builder.Services.AddRazorPages();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "Woodgrove.Intranet";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = sessionLifetime;
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            // No token refresher here — this app calls no API. Only the
            // back-channel-logout revocation check.
            OnValidatePrincipal = DenylistCookieEvents.RejectIfRevokedAsync
        };
    })
    .AddKeycloakOpenIdConnect("keycloak", realm: "woodgrove",
        OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = builder.Configuration["Keycloak:ClientId"];
        options.ClientSecret = builder.Configuration["Keycloak:ClientSecret"];
        options.ResponseType = OpenIdConnectResponseType.Code; // Auth Code + PKCE
        options.UsePkce = true;
        options.RequireHttpsMetadata = false; // DEV ONLY
        // SaveTokens so /auth/logout can send id_token_hint to Keycloak's
        // end-session endpoint (silent RP-initiated logout, no confirm page).
        options.SaveTokens = true;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("roles");
        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.TokenValidationParameters.RoleClaimType = "roles";
        options.SignedOutRedirectUri = "/";
    });

builder.Services.AddAuthorization();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/auth/login", (string? returnUrl) =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = ReturnUrl.Sanitize(returnUrl) }))
    .AllowAnonymous();

app.MapGet("/auth/logout", () =>
    Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
        new[] { CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme }));

// Keycloak POSTs signed logout_tokens here when the SSO session ends
// (registered in keycloak/woodgrove-realm.json as backchannel.logout.url).
app.MapBackchannelLogout("/auth/backchannel-logout");

app.MapRazorPages();

app.Run();

public partial class Program { } // for WebApplicationFactory in tests
```

`src/Intranet/Pages/_ViewImports.cshtml`:

```cshtml
@namespace Intranet.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

`src/Intranet/Pages/Index.cshtml`:

```cshtml
@page
@{
    var isAuthenticated = User.Identity?.IsAuthenticated == true;
}
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <title>Woodgrove Intranet</title>
    <style>
        body { font-family: system-ui; max-width: 720px; margin: 2rem auto; }
        code { background: #f2f2f2; padding: 0 .25rem; }
    </style>
</head>
<body>
    <h1>Woodgrove Intranet (App #2)</h1>
    <p>
        This app is a <em>second OIDC client</em> (<code>intranet</code>) in the same
        Keycloak realm as the React app. If you are already logged in over there,
        logging in here happens <strong>silently via SSO</strong> — no password prompt.
    </p>

    @if (isAuthenticated)
    {
        <p>Signed in as <strong>@User.Identity!.Name</strong></p>
        <p>Roles: @string.Join(", ", User.FindAll("roles").Select(c => c.Value))</p>
        <p>Keycloak session id (<code>sid</code>): <code>@User.FindFirst("sid")?.Value</code></p>
        <p><a href="/auth/logout">Log out (single logout — also ends the React app's session)</a></p>
        <details>
            <summary>All claims</summary>
            <ul>
                @foreach (var claim in User.Claims)
                {
                    <li><code>@claim.Type</code>: @claim.Value</li>
                }
            </ul>
        </details>
    }
    else
    {
        <p><a href="/auth/login">Log in with Keycloak</a></p>
    }

    <hr />
    <p><a href="http://localhost:5173">Open the React app →</a> (port pinned in AppHost)</p>
</body>
</html>
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Intranet.Tests`
Expected: PASS — 4 tests green.

- [ ] **Step 7: Commit**

```bash
git add src/Intranet tests/Intranet.Tests Woodgrovedemo.slnx
git commit -m "feat: Intranet Razor Pages app as second OIDC client with back-channel logout"
```

---

### Task 4: Realm + AppHost wiring, cross-links, and manual E2E verification

**Files:**
- Modify: `keycloak/woodgrove-realm.json`, `src/AppHost/Program.cs`, `src/AppHost/AppHost.csproj`, `src/web/src/App.tsx`

**Interfaces:**
- Consumes: `Intranet` project (Task 3), `/bff/backchannel-logout` (Task 2).
- Produces: Aspire resource `intranet`; realm client `intranet`; back-channel URLs registered on both clients; Vite pinned to port 5173.

- [ ] **Step 1: Update the realm import**

Replace the `"clients"` array in `keycloak/woodgrove-realm.json` with (the `web-bff` entry is unchanged except two new `attributes` keys; `intranet` is new — note it has the roles mapper but **no** audience mapper, because it never calls the API):

```json
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
      "attributes": {
        "post.logout.redirect.uris": "*",
        "backchannel.logout.url": "http://host.docker.internal:5242/bff/backchannel-logout",
        "backchannel.logout.session.required": "true"
      },
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
    },
    {
      "clientId": "intranet",
      "name": "Woodgrove Intranet",
      "enabled": true,
      "protocol": "openid-connect",
      "publicClient": false,
      "secret": "dev-intranet-secret",
      "standardFlowEnabled": true,
      "directAccessGrantsEnabled": false,
      "serviceAccountsEnabled": false,
      "redirectUris": ["*"],
      "webOrigins": ["*"],
      "fullScopeAllowed": true,
      "attributes": {
        "post.logout.redirect.uris": "*",
        "backchannel.logout.url": "http://host.docker.internal:5262/auth/backchannel-logout",
        "backchannel.logout.session.required": "true"
      },
      "protocolMappers": [
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
```

- [ ] **Step 2: Register the Intranet in AppHost and pin the Vite port**

```bash
dotnet add src/AppHost/AppHost.csproj reference src/Intranet/Intranet.csproj
```

In `src/AppHost/Program.cs`, add after the `bff` declaration:

```csharp
// Second OIDC client app — exists to demonstrate SSO + back-channel single logout.
// Its HTTP port (5262) is pinned in launchSettings and baked into the realm's
// backchannel.logout.url (host.docker.internal:5262).
builder.AddProject<Projects.Intranet>("intranet")
    .WithReference(keycloak)
    .WaitFor(keycloak);
```

And change the `web` registration's endpoint line from `.WithHttpEndpoint(env: "PORT")` to:

```csharp
    // Port pinned so the Intranet's "Open the React app" link can be static.
    .WithHttpEndpoint(port: 5173, env: "PORT")
```

- [ ] **Step 3: Add the Intranet link to the React app**

In `src/web/src/App.tsx`, add just before the closing `</main>` (outside the `user ?` ternary so it shows in both states):

```tsx
      <hr />
      <p>
        {/* Port pinned in src/Intranet/Properties/launchSettings.json */}
        <a href="http://localhost:5262">Open the Intranet app (SSO demo) →</a>
      </p>
```

- [ ] **Step 4: Reset the Keycloak volume so the realm re-imports**

Keycloak only imports a realm that doesn't exist yet; the data volume + persistent container keep the old realm (no `intranet` client) alive. Reset:

```bash
docker ps -a --format "{{.Names}}" | grep -i keycloak
docker rm -f <container-name-from-above>
docker volume ls --format "{{.Name}}" | grep -i keycloak
docker volume rm <volume-name-from-above>
```

Expected: container and volume removed. The next AppHost run recreates both and imports the realm with both clients.

- [ ] **Step 5: Build everything, then run the full manual E2E verification**

Run: `dotnet build && dotnet run --project src/AppHost`

Verify in order:

1. Aspire dashboard shows **five** resources healthy: `keycloak`, `api`, `webbff`, `web`, `intranet`.
2. Keycloak admin console (dashboard link, `admin`/`admin`) → realm `woodgrove` → Clients: both `web-bff` and `intranet` exist; each client's Advanced settings show the Backchannel logout URL.
3. Open `http://localhost:5173` → log in as `alice` / `password` → profile loads.
4. **Silent SSO:** open `http://localhost:5262` → "Log in with Keycloak" → you are signed in as alice **without a password prompt**. The page shows roles and a `sid` value.
5. **Single logout, Intranet → React:** click "Log out" on the Intranet. Check the `webbff` console logs in the Aspire dashboard for `Back-channel logout: revoked Keycloak session <sid>`. Reload the React app → you are logged out.
6. Log in again from the React app; confirm the Intranet signs in silently again.
7. **Single logout, React → Intranet:** click "Log out" in the React app. Check the `intranet` logs for the same revocation line. Reload the Intranet → logged out.

**If step 5/7 shows a 400 "Rejected logout token: ... issuer" in the logs instead:** the issuer Keycloak stamps into logout tokens doesn't match the issuer the app discovered. Debug with the logged reason. Known fixes, in order of preference: (a) set a fixed frontend URL on the container in AppHost — `keycloak.WithEnvironment("KC_HOSTNAME", "localhost")` — reset the volume, retest; (b) if the mismatch is only a port/scheme variant of the same realm URL, extend the `TokenValidationParameters` in `BackchannelLogoutEndpoint` to use `ValidIssuers` with both forms and add a comment explaining why. Document whichever fix was needed in the cheatsheet pitfalls (Task 5).

**If step 5/7 shows nothing in the logs at all:** Keycloak couldn't reach `host.docker.internal:<port>`. Check the Keycloak container logs for the failed POST, confirm the port matches launchSettings, and confirm Docker Desktop resolves `host.docker.internal` (it does by default on Windows).

- [ ] **Step 6: Run all test suites**

Run: `dotnet test`
Expected: PASS — Api.Tests (5), AuthShared.Tests, WebBff.Tests (4), Intranet.Tests (4) all green.

- [ ] **Step 7: Commit**

```bash
git add keycloak/woodgrove-realm.json src/AppHost src/web/src/App.tsx
git commit -m "feat: register intranet client + back-channel logout URLs; wire intranet into Aspire"
```

---

### Task 5: Cheatsheet update (MANDATORY — the user studies from this file)

**Files:**
- Modify: `docs/cheatsheet.md` (§4 rewritten; §9 gains three pitfalls; §7 resource list mention)

**Interfaces:** none (documentation). Content below is the baseline; enrich it with anything actually observed in Task 4 (e.g. the issuer fix, real log lines).

- [ ] **Step 1: Rewrite section 4 (`## 4. SSO & Single Logout`)**

Replace the whole section (from `## 4. SSO & Single Logout` up to, not including, `## 5. BFF Pattern`) with:

```markdown
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
```

- [ ] **Step 2: Add three pitfalls to section 9 (`## 9. Common Pitfalls`)**

Append before `## 10. Handy Snippets`:

```markdown
### Back-Channel Logout URL Not Reachable From the Container

**Symptom:** logout in one app doesn't log out the other; no "revoked Keycloak
session" line in the other app's logs; Keycloak logs show a failed POST.
Keycloak runs **inside Docker** — `localhost` there is the container itself. The
realm registers `http://host.docker.internal:<port>/...` so the container can
reach apps on the host, which requires the pinned ports (5242 BFF, 5262 Intranet)
to actually match `launchSettings.json`.

### Logout Token Issuer Mismatch

**Symptom:** the receiving app logs `Rejected logout token: ... issuer`.
The `iss` Keycloak writes into logout tokens must equal the issuer the app saw in
the discovery document. If Keycloak's hostname settings produce a different
URL for backend-initiated tokens than for browser-facing discovery, validation
fails closed. Fix by pinning the container's hostname (e.g. `KC_HOSTNAME`) so
both views agree.

### Stale Cookie Without a `sid` Claim

**Symptom:** a session created *before* back-channel logout was added never gets
revoked. The denylist keys on the `sid` claim; a cookie principal without one is
skipped (`DenylistCookieEvents`). Fix: log out/in once to mint a fresh session.
In-memory denylist is also single-instance and empties on restart — dev-only
simplification; production uses a distributed cache.
```

- [ ] **Step 3: Verify the cheatsheet renders**

Skim `docs/cheatsheet.md` in a Markdown preview: §4 diagram fences render, table pipes aligned, no broken heading levels (all `###` under `## 4` / `## 9`).

- [ ] **Step 4: Commit**

```bash
git add docs/cheatsheet.md
git commit -m "docs(cheatsheet): SSO now demonstrated - back-channel logout section and pitfalls"
```

---

### Task 6: README + final verification

**Files:**
- Modify: `README.md`

**Interfaces:** none.

- [ ] **Step 1: Update the README**

Make these changes to `README.md`:

1. **"What This Demonstrates"** — add two bullets:

```markdown
- Real SSO: a second app (`intranet` client) logs in silently — no password prompt
- Single logout via OIDC Back-Channel Logout — logout in either app ends both sessions
```

2. **Architecture diagram** — replace the existing diagram with:

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

3. **Aspire Resources table** — add:

```markdown
| `intranet` | .NET project      | Razor Pages second OIDC client — SSO + back-channel logout demo (`src/Intranet`, http://localhost:5262) |
```

4. **Key Endpoints table** — add:

```markdown
| `GET /auth/login` (intranet)      | —    | Triggers OIDC challenge (silent if SSO session exists) |
| `GET /auth/logout` (intranet)     | —    | Single logout — ends both apps' sessions |
| `POST /bff/backchannel-logout`    | —    | Keycloak-only: receives signed logout tokens |
| `POST /auth/backchannel-logout`   | —    | Keycloak-only: receives signed logout tokens |
```

5. **Running the Tests** — replace the ".NET integration tests (5 tests)" heading/body with:

```markdown
### .NET tests

```bash
dotnet test
```

- `tests/Api.Tests` — public/401 + role-based endpoint tests (5).
- `tests/AuthShared.Tests` — logout-token validation, sid denylist, returnUrl sanitizer.
- `tests/WebBff.Tests` — BFF smoke tests + back-channel logout integration (4).
- `tests/Intranet.Tests` — Intranet smoke tests + back-channel logout integration (4).
```

6. **Manual Verification Checklist** — add at the end:

```markdown
- [ ] **Silent SSO login** — Log in on the React app, open http://localhost:5262, click "Log in": you are signed in with no password prompt.
- [ ] **Single logout (Intranet → React)** — Log out on the Intranet; reload the React app: logged out. `webbff` logs show "Back-channel logout: revoked Keycloak session".
- [ ] **Single logout (React → Intranet)** — Log back in, log out from the React app; reload the Intranet: logged out. `intranet` logs show the revocation line.
```

7. **Test Users note** — after the admin-console paragraph, add:

```markdown
> `dev-intranet-secret` (like `dev-bff-secret`) is a hardcoded **dev-only** client secret for the realm import. Production: real secret management, exact redirect URIs, HTTPS back-channel URLs.
```

8. **Source Map** — add rows:

```markdown
| `src/Intranet/Program.cs`             | Second OIDC client (SSO + single logout demo) |
| `src/AuthShared/`                     | Logout-token validator, sid denylist, back-channel endpoint |
```

- [ ] **Step 2: Run everything one last time**

```bash
dotnet test
cd src/web && npx vitest run && cd ../..
```

Expected: all .NET suites green; both React tests green.

- [ ] **Step 3: Walk the updated manual checklist**

Run `dotnet run --project src/AppHost` and walk the three new checklist items end-to-end. Fix gaps before committing.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: README - intranet resource, SSO/single-logout checklist, new test projects"
```

---

## Self-Review

**Spec coverage:**
- Silent SSO via second client → Tasks 3, 4 (spec §5.1). ✅
- Back-channel logout both directions → Tasks 1, 2, 3, 4 (spec §5.2). ✅
- `sid` denylist + cookie event, composing with TokenRefresher → Tasks 1, 2 (spec §2, §4.2, §4.3). ✅
- Logout-token validation rules incl. `nonce` prohibition → Task 1 (spec §4.2). ✅
- Pinned ports + `host.docker.internal` back-channel URLs + `session.required` → Task 4 (spec §3, §4.4). ✅
- Intranet lean (no API calls; `SaveTokens` only for `id_token_hint`) → Task 3 (spec §4.1; the spec said "no SaveTokens custody" — kept for the end-session hint, deviation documented in code comment and cheatsheet). ✅
- `returnUrl` validation on Intranet login → Tasks 1, 3 (spec §6). ✅
- Error handling: 400/log-only, TTL bound, no-sid pitfall → Tasks 1, 5 (spec §6). ✅
- Tests: AuthShared unit, WebBff smoke (closes old spec §7 gap) + valid-token integration, Intranet smoke → Tasks 1–3 (spec §7). ✅
- Cheatsheet §4 rewrite + pitfalls, README updates → Tasks 5, 6 (spec §8). ✅
- Success criteria (5 healthy resources, SSO observable, both-direction logout, suites green) → Task 4 steps 5–6, Task 6 (spec §9). ✅

**Placeholder scan:** no TBD/TODO; all code inline; Task 4's issuer-mismatch contingency is a debugging procedure with concrete fixes, not a placeholder. ✅

**Type consistency:** `ISessionDenylist.Revoke/IsRevoked(string)`, `AddSessionDenylist(TimeSpan)`, `LogoutTokenValidator.ValidateAsync(string, TokenValidationParameters)` → `LogoutTokenValidationResult(IsValid, Sid, Error)`, `MapBackchannelLogout(path)`, `ReturnUrl.Sanitize(string?)`, `TestTokens.CreateLogoutToken(issuer, audience, sid, includeEvents, nonce, signingKey)` used identically in Tasks 1, 2, 3. Cookie names, ports (5242/5262/5173), and endpoint paths match across Tasks 2–6. ✅

**Note for implementer:** Two spots may need adjustment against reality: (1) the logout-token `iss` Keycloak emits for container-initiated POSTs (Task 4 Step 5 has the debug procedure); (2) whether the `sid` claim survives into the BFF's cookie principal with `GetClaimsFromUserInfoEndpoint = true` — verify via `/bff/user` claims during Task 4; if missing, add `options.ClaimActions.MapUniqueJsonKey("sid", "sid");` to the BFF's OIDC options.
