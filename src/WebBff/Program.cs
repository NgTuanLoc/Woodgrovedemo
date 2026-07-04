using System.Security.Claims;
using AuthShared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// One lifetime for both: a denylist entry only needs to outlive the cookies
// that can carry its sid.
var sessionLifetime = TimeSpan.FromHours(8);
builder.Services.AddSessionDenylist(sessionLifetime);

// In dev the SPA is served by the Vite dev server on a different origin and
// proxies /bff, /api and the OIDC callback to this BFF. Honor the forwarded
// host/proto Vite sends (xfwd) so the OIDC redirect_uri is built on the SPA
// origin and the browser returns to the SPA (not the BFF) after login.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

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
        // SameAsRequest keeps the cookie usable over plain HTTP in local dev;
        // set CookieSecurePolicy.Always in production (HTTPS-only).
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = sessionLifetime;
        options.SlidingExpiration = true;
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

// Apply forwarded headers before anything reads scheme/host (dev only; the Vite
// dev server is the trusted proxy). In production the BFF is the single origin
// and no forwarding is needed.
if (app.Environment.IsDevelopment())
    app.UseForwardedHeaders();

app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();

// --- BFF endpoints ---

app.MapGet("/bff/login", (string? returnUrl) =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = ReturnUrl.Sanitize(returnUrl) }))
    .AllowAnonymous();

app.MapGet("/bff/logout", (string? returnUrl) =>
    Results.SignOut(new AuthenticationProperties { RedirectUri = ReturnUrl.Sanitize(returnUrl) },
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
            try
            {
                string Pad(string s) => s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=')
                    .Replace('-', '+').Replace('_', '/');
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Pad(parts[1])));
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return new { present = true, payload = doc.RootElement.Clone() };
            }
            catch
            {
                return new { present = true, decoded = "(unparseable)" };
            }
        }

        return Results.Ok(new
        {
            id_token = Decode(await ctx.GetTokenAsync("id_token")),
            access_token = Decode(await ctx.GetTokenAsync("access_token")),
            expires_at = await ctx.GetTokenAsync("expires_at")
        });
    });
}

// Keycloak POSTs signed logout_tokens here when the SSO session ends
// (registered in keycloak/woodgrove-realm.json as backchannel.logout.url).
app.MapBackchannelLogout("/bff/backchannel-logout");

app.MapReverseProxy();

app.Run();

public partial class Program { } // for WebApplicationFactory in tests
