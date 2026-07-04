using AuthShared.Tests;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Intranet.Tests;

public class IntranetTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<OpenIdConnectOptions>(
                OpenIdConnectDefaults.AuthenticationScheme, options =>
                {
                    var config = new OpenIdConnectConfiguration
                    {
                        Issuer = TestTokens.Issuer,
                        AuthorizationEndpoint =
                            TestTokens.Issuer + "/protocol/openid-connect/auth",
                        TokenEndpoint =
                            TestTokens.Issuer + "/protocol/openid-connect/token",
                        EndSessionEndpoint =
                            TestTokens.Issuer + "/protocol/openid-connect/logout"
                    };
                    config.SigningKeys.Add(TestTokens.SigningKey);
                    options.Configuration = config;
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(config);
                });
        });
    }
}
