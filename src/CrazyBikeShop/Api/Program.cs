using System.Diagnostics;
using CrazyBikeShop.ServiceDefaults;
using CrazyBikeShop.Shared;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
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

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.MapPost("/schedule", async ([FromBody] Schedule schedule, [FromServices] DurableTaskClient durableTaskClient) =>
    {
        await RunSequentialOrchestrations(schedule, durableTaskClient);
        return Results.Ok(new { Message = "Orchestrations scheduled and completed." });
    })
    .WithName("ScheduleOrchestration");

app.Run();
return;

async Task RunSequentialOrchestrations(Schedule schedule, DurableTaskClient client)
{
    var completedOrchestrations = 0;     // Track total completed orchestrations
    var failedOrchestrations = 0;        // Track total failed orchestrations
    
    var orchestrations = Enumerable.Range(1, schedule.TotalOrchestrations).Select(_ => new StartBikeOrderOrchestrator
    {
        InstanceId = Guid.NewGuid().ToString(),
        Bike = CrazyBikeSelector.GetOne()
    }).ToList();
    
    // List to track all instance ids for monitoring
    var allInstanceIds = orchestrations.Select(o => o.InstanceId).ToList();
    
    // Schedule each orchestration with delay between them
    foreach (var (o, i) in orchestrations.Select((orchestrator, index) => (orchestrator, index)))
    {
        // Create a unique instance ID
        app.Logger.LogInformation("Scheduling orchestration #{Number} ({InstanceName})", i+1, o.InstanceId);
        
        try
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Schedule the orchestration
            await client.ScheduleNewOrchestrationInstanceAsync(
                "CrazyBikeOrchestration", 
                o.Bike, new StartOrchestrationOptions{ InstanceId = o.InstanceId });
            
            stopwatch.Stop();
            
            app.Logger.LogInformation("Orchestration #{Number} scheduled in {ElapsedMs}ms with ID: {InstanceId}", 
                i+1, stopwatch.ElapsedMilliseconds, o.InstanceId);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Error scheduling orchestration #{Number}", i+1);
        }
        
        // Wait before scheduling next orchestration (except for the last one)
        if (i < schedule.TotalOrchestrations - 1)
        {
            app.Logger.LogInformation("Waiting {Seconds} seconds before scheduling next orchestration...", schedule.IntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(schedule.IntervalSeconds));
        }
    }
    
    app.Logger.LogInformation("All {Count} orchestrations scheduled. Waiting for completion...", allInstanceIds.Count);

    // Now wait for all orchestrations to complete
    foreach (var id in allInstanceIds)
    {
        try
        {
            var instance = await client.WaitForInstanceCompletionAsync(
                id, getInputsAndOutputs: false, CancellationToken.None);

            switch (instance.RuntimeStatus)
            {
                case OrchestrationRuntimeStatus.Completed:
                    completedOrchestrations++;
                    app.Logger.LogInformation("Orchestration {Id} completed successfully", instance.InstanceId);
                    break;
                case OrchestrationRuntimeStatus.Failed:
                    failedOrchestrations++;
                    app.Logger.LogError("Orchestration {Id} failed: {ErrorMessage}", 
                        instance.InstanceId, instance.FailureDetails?.ErrorMessage);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Error waiting for orchestration {Id} completion", id);
        }
    }
    
    // Log final stats
    app.Logger.LogInformation("FINAL RESULTS: {Completed} completed, {Failed} failed, {Total} total orchestrations", 
        completedOrchestrations, failedOrchestrations, allInstanceIds.Count);
}