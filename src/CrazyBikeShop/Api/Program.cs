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

app.MapPost("/schedule", async ([FromServices] DurableTaskClient durableTaskClient) =>
{
    var scheduleRequest = new ScheduleRequest
    {
        Id = Guid.NewGuid().ToString(),
        OrchestrationName = "CrazyBikeOrchestrator",
        Input = CrazyBikeSelector.GetOne()
    };
    
    try
    {
        var startOrchestrationOptions = new StartOrchestrationOptions
        {
            InstanceId = scheduleRequest.Id
        };
        
        var instanceId = await durableTaskClient.ScheduleNewOrchestrationInstanceAsync(scheduleRequest.OrchestrationName, scheduleRequest.Input, startOrchestrationOptions);
        //var metadata = await durableTaskClient.WaitForInstanceStartAsync(instanceId);

        app.Logger.LogInformation("Created new schedule with ID: {ScheduleId}", scheduleRequest.Id);
        
        return Results.CreatedAtRoute("GetOrchestration", new { id = scheduleRequest.Id });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error creating schedule {ScheduleId}", scheduleRequest.Id);
        return Results.InternalServerError(ex.Message);
    }
})
.WithName("ScheduleOrchestration");

app.MapGet("/schedule/{id}", async (string id, 
        [FromServices] DurableTaskClient durableTaskClient) =>
{
    try
    {
        var metadata = await durableTaskClient.GetInstanceAsync(id);
        return Results.Ok(metadata);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving schedule {ScheduleId}", id);
        return Results.InternalServerError(ex.Message);
    }
})
.WithName("GetOrchestration");

app.Run();