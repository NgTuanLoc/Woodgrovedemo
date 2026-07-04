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
