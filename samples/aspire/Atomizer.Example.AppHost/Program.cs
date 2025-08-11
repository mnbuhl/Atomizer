var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder
    .AddPostgres("postgres")
    .WithPgAdmin()
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

builder.AddProject<Projects.Atomizer_Example>("atomizer-example");

builder
    .AddProject<Projects.Atomizer_EFCore_Example>("atomizer-efcore-example")
    .WithReference(postgres)
    .WaitFor(postgres);

builder.Build().Run();
