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
