var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres").WithPgAdmin().WithDataVolume();

var database = postgres.AddDatabase("atomizerdb");

builder.AddProject<Projects.Atomizer_Example>("atomizer-example");

builder
    .AddProject<Projects.Atomizer_EFCore_Example>("atomizer-efcore-example")
    .WithReference(database)
    .WaitFor(database);

builder.Build().Run();
