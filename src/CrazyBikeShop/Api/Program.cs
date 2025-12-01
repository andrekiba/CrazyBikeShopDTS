using System.Diagnostics;
using System.Text.Json;
using Api;
using CrazyBikeShop.ServiceDefaults;
using CrazyBikeShop.Shared;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.ScheduledTasks;
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

app.MapPost("/schedule", async ([FromBody] ScheduleRequest scheduleRequest, [FromServices] ScheduledTaskClient scheduledTaskClient) =>
{
    try
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, CrazyBikeSelector.GetOne());
        stream.Position = 0;
        var input = new StreamReader(stream).ReadToEnd();
        
        var creationOptions = new ScheduleCreationOptions(scheduleRequest.Id, scheduleRequest.OrchestrationName, scheduleRequest.Interval)
        {
            //OrchestrationInput = scheduleRequest.Input,
            OrchestrationInput = input, 
            StartAt = scheduleRequest.StartAt,
            EndAt = scheduleRequest.EndAt,
            StartImmediatelyIfLate = true
        };

        var scheduleClient = await scheduledTaskClient.CreateScheduleAsync(creationOptions);
        var description = await scheduleClient.DescribeAsync();

        app.Logger.LogInformation("Created new schedule with ID: {ScheduleId}", scheduleRequest.Id);

        //await RunSequentialOrchestrations(scheduleRequest, durableTaskClient);
        //return Results.Ok(new { Message = "Orchestrations scheduled and completed." });
        
        return Results.CreatedAtRoute("GetOrchestration", new { id = scheduleRequest.Id }, description);
    }
    catch (ScheduleClientValidationException ex)
    {
        app.Logger.LogError(ex, "Validation failed while creating schedule {ScheduleId}", scheduleRequest.Id);
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error creating schedule {ScheduleId}", scheduleRequest.Id);
        return Results.InternalServerError(ex.Message);
    }
})
.WithName("ScheduleOrchestration");

app.MapGet("/schedule/{id}", async (string id, [FromServices] ScheduledTaskClient scheduledTaskClient) =>
{
    try
    {
        var schedule = await scheduledTaskClient.GetScheduleAsync(id);
        return Results.Ok(schedule);
    }
    catch (ScheduleNotFoundException)
    {
        return Results.NotFound();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving schedule {ScheduleId}", id);
        return Results.InternalServerError(ex.Message);
    }
})
.WithName("GetOrchestration");

app.Run();
return;

async Task RunSequentialOrchestrations(BatchScheduleRequest batchScheduleRequest, DurableTaskClient client)
{
    var completedOrchestrations = 0;     // Track total completed orchestrations
    var failedOrchestrations = 0;        // Track total failed orchestrations
    
    var orchestrations = Enumerable.Range(1, batchScheduleRequest.TotalOrchestrations).Select(_ => new StartBikeOrderOrchestrator
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
        if (i < batchScheduleRequest.TotalOrchestrations - 1)
        {
            app.Logger.LogInformation("Waiting {Seconds} seconds before scheduling next orchestration...", batchScheduleRequest.IntervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(batchScheduleRequest.IntervalSeconds));
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