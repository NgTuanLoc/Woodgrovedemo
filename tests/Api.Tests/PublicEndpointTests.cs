using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

public class PublicEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public PublicEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Public_endpoint_returns_200_without_token()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/public");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Me_endpoint_returns_401_without_token()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
