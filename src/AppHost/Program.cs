var builder = DistributedApplication.CreateBuilder(args);

// Pin the admin password (dev only) so the admin console login is stable and
// matches the docs (username admin / password admin) instead of an auto-generated one.
var keycloakPassword = builder.AddParameter("kc-admin-password", "admin", secret: true);

// NOTE: Keycloak only imports a realm if it doesn't already exist. With a data
// volume + persistent container the realm survives restarts, so edits to
// woodgrove-realm.json (or the admin password above) are ignored until you
// remove the data volume:
//   docker rm -f <keycloak-container> && docker volume rm <apphost>-keycloak-data
// The Keycloak integration upgrades the endpoint to HTTPS with an Aspire dev cert
// in run mode and binds it to a *dynamic* host port (the `8080` below is nominal —
// the run-mode HTTPS upgrade overrides it, and there is no public opt-out in this
// preview package). Reach the admin console via the `keycloak` link in the Aspire
// dashboard; the admin login is pinned to admin / admin above.
var keycloak = builder.AddKeycloak("keycloak", 8080, adminPassword: keycloakPassword)
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
