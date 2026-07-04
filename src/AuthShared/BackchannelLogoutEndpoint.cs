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
