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
