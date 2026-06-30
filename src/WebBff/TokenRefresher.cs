using System.Globalization;
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

        // Fix 1: guard ConfigurationManager null — reject instead of throwing NullReferenceException
        if (oidcOptions.ConfigurationManager is null)
        {
            context.RejectPrincipal();
            return;
        }

        try
        {
            // Fix 2: wrap the entire network + parse path in try/catch so any exception
            // (network, timeout, JSON parse, etc.) results in a graceful re-login, not a 500.
            var config = await oidcOptions.ConfigurationManager
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

            // Fix 3: use TryGetProperty so a missing or wrong-type field rejects instead of throws
            if (!root.TryGetProperty("access_token", out var accessTokenEl)
                || accessTokenEl.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                context.RejectPrincipal();
                return;
            }
            var newAccess = accessTokenEl.GetString()!;

            if (!root.TryGetProperty("expires_in", out var expiresInEl))
            {
                context.RejectPrincipal();
                return;
            }
            var expiresIn = expiresInEl.GetInt32();

            var newRefresh = root.TryGetProperty("refresh_token", out var rt)
                ? rt.GetString()! : refreshToken.Value;
            var newExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
                .ToString("o", CultureInfo.InvariantCulture);

            context.Properties.UpdateTokenValue("access_token", newAccess);
            context.Properties.UpdateTokenValue("refresh_token", newRefresh);
            context.Properties.UpdateTokenValue("expires_at", newExpiresAt);
            if (root.TryGetProperty("id_token", out var idt))
                context.Properties.UpdateTokenValue("id_token", idt.GetString()!);

            context.ShouldRenew = true; // re-issue the cookie with updated tokens
        }
        catch (Exception)
        {
            context.RejectPrincipal();
        }
    }
}
