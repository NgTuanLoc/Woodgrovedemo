var builder = DistributedApplication.CreateBuilder(args);

// NOTE: Keycloak only imports a realm if it doesn't already exist. With a data
// volume + persistent container the realm survives restarts, so edits to
// woodgrove-realm.json are ignored until you remove the data volume:
//   docker rm -f <keycloak-container> && docker volume rm <apphost>-keycloak-data
var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume()
    .WithRealmImport("../../keycloak");

var api = builder.AddProject<Projects.Api>("api")
    .WithReference(keycloak)
    .WaitFor(keycloak);

var bff = builder.AddProject<Projects.WebBff>("webbff")
    .WithReference(keycloak)
    .WithReference(api)
    .WaitFor(keycloak)
    .WaitFor(api);

builder.AddNpmApp("web", "../web", "dev")
    .WithReference(bff)
    .WaitFor(bff)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
