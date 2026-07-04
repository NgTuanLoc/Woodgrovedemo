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
