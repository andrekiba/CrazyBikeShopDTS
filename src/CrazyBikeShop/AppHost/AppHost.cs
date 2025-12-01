var builder = DistributedApplication.CreateBuilder(args);

var scheduler =
    builder.AddContainer("scheduler", "mcr.microsoft.com/dts/dts-emulator", "latest")
        .WithHttpEndpoint(name: "grpc", targetPort: 8080)
        .WithHttpEndpoint(name: "dashboard", targetPort: 8082);

var schedulerConnectionString = ReferenceExpression.Create($"Endpoint={scheduler.GetEndpoint("grpc")};TaskHub=default;Authentication=None");

//builder.AddProject<Projects.Client>("client");
builder.AddProject<Projects.Api>("api")
    //.WithReference(scheduler)
    .WaitFor(scheduler);

builder.AddProject<Projects.Orchestrator>("orchestrator");

builder.AddProject<Projects.Assembler>("assembler");

builder.AddProject<Projects.Ship>("ship");

builder.Build().Run();
