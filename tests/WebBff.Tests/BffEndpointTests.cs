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
