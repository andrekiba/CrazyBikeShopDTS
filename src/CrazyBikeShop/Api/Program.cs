using Api;
using CrazyBikeShop.ServiceDefaults;
using CrazyBikeShop.Shared;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddConsole();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.All;
});

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDurableTaskClient(b =>
{
    b.UseDurableTaskScheduler(Environment.GetEnvironmentVariable("ConnectionStrings__dts")!);
});

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

app.MapPost("/buy", async ([FromServices] DurableTaskClient durableTaskClient) =>
{
    var orchestrationRequest = new OrchestrationRequest
    {
        Id = Guid.NewGuid().ToString(),
        OrchestrationName = "CrazyBikeOrchestrator",
        Input = CrazyBikeSelector.GetOne()
    };
    
    try
    {
        var startOrchestrationOptions = new StartOrchestrationOptions
        {
            InstanceId = orchestrationRequest.Id
        };
        
        var instanceId = await durableTaskClient.ScheduleNewOrchestrationInstanceAsync(orchestrationRequest.OrchestrationName, orchestrationRequest.Input, startOrchestrationOptions);
        //var metadata = await durableTaskClient.WaitForInstanceStartAsync(instanceId);

        app.Logger.LogInformation("Created new orchestration with ID: {Orchestration}", orchestrationRequest.Id);
        
        return Results.CreatedAtRoute("GetOrchestration", new { id = orchestrationRequest.Id });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error creating orchestration {Orchestration}", orchestrationRequest.Id);
        return Results.InternalServerError(ex.Message);
    }
})
.WithName("Buy");

app.MapGet("/orchestration/{id}", async (string id, 
        [FromServices] DurableTaskClient durableTaskClient) =>
{
    try
    {
        var metadata = await durableTaskClient.GetInstanceAsync(id);
        return Results.Ok(metadata);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving orchestration {Orchestration}", id);
        return Results.InternalServerError(ex.Message);
    }
})
.WithName("GetOrchestration");

app.Run();