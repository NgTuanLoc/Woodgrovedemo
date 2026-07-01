var builder = DistributedApplication.CreateBuilder(args);

// No data volume / persistent lifetime on purpose: the realm JSON is the source
// of truth for this demo, and Keycloak only imports a realm if it doesn't already
// exist. A persisted realm would silently ignore edits to woodgrove-realm.json.
// Each run starts fresh and re-imports. Add .WithDataVolume() once your realm is
// stable if you want to keep runtime-created data across restarts.
var keycloak = builder.AddKeycloak("keycloak", 8080)
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
