var builder = DistributedApplication.CreateBuilder(args);

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume()
    .WithRealmImport("../../keycloak");

var api = builder.AddProject<Projects.Api>("api")
    .WithReference(keycloak)
    .WaitFor(keycloak);

builder.Build().Run();
