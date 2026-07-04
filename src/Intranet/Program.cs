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
