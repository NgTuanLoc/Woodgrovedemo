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
