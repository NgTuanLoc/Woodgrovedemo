using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Replaces the real Keycloak JWT validation with a test scheme driven by request headers:
//   X-Test-User: <name>    X-Test-Roles: admin,user
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o,
        ILoggerFactory l, UrlEncoder e) : base(o, l, e) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new(RolesClaimsHelper.NameClaimType, user!) };
        if (Request.Headers.TryGetValue("X-Test-Roles", out var roles))
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => new Claim(RolesClaimsHelper.RoleClaimType, r.Trim())));

        var identity = new ClaimsIdentity(claims, "Test",
            RolesClaimsHelper.NameClaimType, RolesClaimsHelper.RoleClaimType);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity),
            JwtBearerDefaults.AuthenticationScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class ProtectedEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public ProtectedEndpointTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            // AddKeycloakJwtBearer already registered a "Bearer" scheme via
            // IConfigureOptions<AuthenticationOptions>. Removing those descriptors
            // before re-registering with TestAuthHandler prevents "Scheme already exists".
            var toRemove = s
                .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                .ToList();
            foreach (var d in toRemove) s.Remove(d);

            s.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
             .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                 JwtBearerDefaults.AuthenticationScheme, _ => { });
        }));

    private HttpClient ClientFor(string user, string roles)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-User", user);
        c.DefaultRequestHeaders.Add("X-Test-Roles", roles);
        return c;
    }

    [Fact]
    public async Task Me_returns_200_for_authenticated_user()
    {
        var res = await ClientFor("bob", "user").GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Admin_returns_403_for_non_admin()
    {
        var res = await ClientFor("bob", "user").GetAsync("/api/admin");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Admin_returns_200_for_admin()
    {
        var res = await ClientFor("alice", "admin,user").GetAsync("/api/admin");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
